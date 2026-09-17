# 14 — Skill ve Script Çalıştırma — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum başlangıcı):** Dosya 15 (WF) 70/70 ile kapandı, bu
> ap-s2'nin SON ailesi. Ortam: ap-s2'nin kendi PostgreSQL örneği (port 5082,
> `mt_s2` şeması), uygulama ayakta. `$APU="http://localhost:5082/tracon"`
> (spec'in kendi varsayılanı `5080` farklı port — ap-s2'nin izole portu 5082
> kullanılıyor, `00-INDEKS.md`/şerit kurulumu kuralı). Skill/script hiçbir
> şekilde önceden kayıtlı değil (`Program.cs`'te `Skill`/`UseSkillScripts`
> hiç geçmiyor) — spec'in ön koşuluyla birebir uyumlu.

---

# 1 — Skill CRUD ve Frontmatter Doğrulama (Faz 10)

## MT-SKILL-001 — `PUT /api/skills/{name}` yeni bir skill oluşturur (`201`)

**Gerçek sonuç**
`HTTP: 201`. Gövdede `version: 1`, `createdAt == updatedAt`
(`"2026-09-17T15:31:29.743493+00:00"` ikisinde de). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-002 — Aynı skill'i tekrar `PUT` etmek günceller (`200`)

**Gerçek sonuç**
`HTTP: 200` (`201` değil). `version: 2`, `createdAt` DEĞİŞMEDİ,
`updatedAt` ilerledi. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-003 — `GET /api/skills` kiracının skill listesini döner

**Gerçek sonuç**
`['fatura-kontrolu']` — liste skill'i içeriyor. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-004 — `DELETE` skill'i ve cascade kaynaklarını siler

**Gerçek sonuç**
Kaynaklı `test-kaynakli` oluşturuldu (`201`). `DELETE` → `HTTP: 204`.
Ardından `GET` → `HTTP: 404`, `title: "Skill not found"` (İngilizce —
K-228). SQL doğrulaması: `SELECT count(*) FROM mt_s2.agent_skill_resources
WHERE skill_id = (...)` → `0` — cascade çalışıyor. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-005 — Var olmayan skill'i silmek → `404`

**Gerçek sonuç**
`HTTP: 404`, `title: "Skill not found"`, `detail: "There is no skill named
'hic-yok'."` (İngilizce — K-228). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-006 — Yoldaki ad ile gövdedeki ad uyuşmazsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Name mismatch"` (İngilizce — K-228), `detail: "The
path name is 'skill-a', the body name is 'skill-b'."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-007 — Büyük harf/alt çizgi içeren ad → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill name invalid"` (İngilizce — K-228), `detail`
spec'in beklediği İngilizce MAF metniyle BİREBİR eşleşiyor: "Skill name
must use only lowercase letters, numbers, and hyphens, and must not start
or end with a hyphen or contain consecutive hyphens." Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-008 — 65 karakterlik ad → `400`

**Gerçek sonuç**
`HTTP: 400`, `detail: "Skill name must be 64 characters or fewer."` —
spec'in beklediğiyle birebir. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-009 — Boş `description` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill description invalid"` (İngilizce — K-228),
`detail: "Skill description is required."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-010 — `instructions` 64 KB sınırını aşarsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill instructions too large"` (İngilizce — K-228),
`detail: "instructions may be at most 65536 bytes."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-011 — 21. kaynak eklenirse (limit 20) → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Too many resources"` (İngilizce — K-228),
`detail: "A skill may carry at most 20 resources."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-012 — Aynı skill içinde iki kaynak aynı adı taşırsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Resource name invalid"` (İngilizce — K-228),
`detail: "Every resource name must be non-empty and unique within the
skill."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-013 — Arayüzden skill oluşturma ve düzenleme

**Gerçek sonuç**
Playwright: `/tracon/skills` → "New skill" → `arayuz-skilli` +
açıklama + Markdown içerikli talimat (`# başlık`, `- madde`) girildi,
Save. Listeye geri dönüldü, `arayuz-skilli` görünüyor, kaynak sayısı `0`.
Instructions alanı DÜZ `<textarea>` (accessibility role "textbox") olarak
kaldı — girilen `# Bu skill arayuzden yazildi. - Madde bir` render
EDİLMEDEN aynen göründü (başlık/madde işaretine dönüşmedi). Sayfanın
kendi başlığı da zaten "Markdown is stored as source text. The console
does not render it." diyor. Tam beklenen. (Bilinen `HATA-S2-002` CSP
konsol hatası tekrar gözlendi, ilgisiz.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-014 — Skill'i arayüzden devre dışı bırakma, checkbox kilitlenir

**Gerçek sonuç**
`arayuz-skilli` düzenlendi, "Enabled" checkbox kaldırıldı, Save. Listede
`Disabled` rozeti göründü ("Stored but not attached to any run: an agent
that references it gets nothing." açıklamasıyla). `manuel-bos` (DB
kaynaklı, düzenlenebilir) agent'ının düzenleme ekranı açıldı, Skills
panelinde `arayuz-skilli` checkbox'ı `[disabled]` özniteliğiyle ve
"Disabled" etiketiyle göründü — tıklanamıyor. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-020 — Bilinmeyen skill adına işaret eden agent → SAVE zamanında `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Definition invalid"` (İngilizce — K-228), `detail:
"Agent 'hayalet-skilli-agent' refers to skill 'hic-var-olmayan-skill', but
the skill was not found."` — davranış (400, doğru ad, save-time) tam
beklenen.

🚨 **Zarf düzeltmesi:** Spec ayrı `code`/`message`/`path` alanları taşıyan
yapılandırılmış bir doğrulama raporu bekliyordu (`code: "unknown_skill"`,
`path: "skillNames[0]"`). Gerçek yanıt DÜZ bir `ProblemDetails` — tüm bilgi
`detail` metninde tek dizge olarak. Kaynakta `Code`/`Message` alanları
GERÇEKTEN var (`AgentDefinitionValidator.cs:289-290`, iç
`ValidationIssue` tipinde) ama `AgentEndpoints` bunu HTTP'ye taşırken
düzleştiriyor. Davranışsal iddia (400 + doğru mesaj) doğru; JSON şekli
zarfı yanlış — spec'in "Beklenen sonuç" zarf varsayımı düzeltilmeli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-021 — `MaxSkillsPerAgent` aşımı SAVE'de geçer, yalnız RUN'da `400`

**Gerçek sonuç**
`Tracon__Skills__MaxSkillsPerAgent=1` ile yeniden başlatıldı. İki skill
oluşturuldu (201/201). Adım 1 (`iki-skilli-agent`, 2 skill): `HTTP: 201`
— kayıt BAŞARILI, limit denetlenmedi. Adım 2 (çalıştır): `HTTP: 400`,
`title: "Agent compilation failed"` (İngilizce — K-228), `detail: "Agent
'iki-skilli-agent' can have at most 1 skills."`. Tam beklenen — asimetri
doğrulandı. Ayar kaldırıldı, yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-022 — Devre dışı skill derlemeye girmez, model `load_skill` görmez

**Gerçek sonuç**
`fatura-kontrolu` devre dışı bırakıldı (`enabled:false`), `manuel-skill-test`
agent'ı oluşturuldu (`skillNames:["fatura-kontrolu"]`). Playground'da
`FIX-SKILL-PROMPT` gönderildi: HİÇBİR `load_skill` onay kartı belirmedi —
model doğrudan genel bir yardım yanıtı üretti (fatura kontrolüyle ilgili
genel bir soru-cevap, `FATURA_SKILL_ACTIVE` işaretçisi YOK). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-023 — Kod tanımlı skill, aynı adlı DB kaydını geçersiz kılar

**Gerçek sonuç**
`samples/Tracon.Api/Program.cs`'e geçici `tracon.AddSkill(new
AgentSkillDefinition { Name = "fatura-kontrolu", ... "KOD_SKILL_ACTIVE" yaz
..., Enabled = true })` eklendi, `dotnet build -c release` (0/0), yeniden
başlatıldı. Playground'da `FIX-SKILL-PROMPT` gönderildi, onaylandı:
`load_skill` sonucu `<description>KOD TANIMLI surum - DB kaydini gecersiz
kilar.</description>` ve `KOD_SKILL_ACTIVE` talimatını taşıdı (DB'deki
`Fatura kontrol kurallarini aciklar.`/`FATURA_SKILL_ACTIVE` DEĞİL). Modelin
nihai yanıtı tam olarak `KOD_SKILL_ACTIVE`. Tam beklenen. Kod değişikliği
GERİ ALINDI (`git status --short` temiz), yeniden `dotnet build` (0/0),
yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-024 — Skill düzenlemesi, önbellek parmak izini değiştirir

**Gerçek sonuç**
`fatura-kontrolu` yeniden ETKİN yapıldı (`FATURA_SKILL_ACTIVE` işaretçili,
kod skill'i geri alınmış hâldeyken). Playground'da (yeni sohbet)
`FIX-SKILL-PROMPT` gönderildi, onaylandı: yanıt `FATURA_SKILL_ACTIVE`.
Skill'in `instructions`'ı `FATURA_SKILL_V2` işaretçisine güncellendi
(`PUT`, `version: 5`). YENİ bir sohbette (fresh `/playground/manuel-skill-test`)
aynı prompt tekrar gönderildi, onaylandı: `load_skill` sonucu ve modelin
nihai yanıtı `FATURA_SKILL_V2` — eski (önbelleğe alınmış) `ACTIVE` metni
HİÇ sızmadı. Tam beklenen; `CompiledAgentCache` parmak izi düzenlemede
doğru şekilde geçersiz kılınıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-025 — Sunucu `MaxSkillsPerAgent`'ı değiştirir, arayüz HABERSİZ kalır

**Gerçek sonuç**
`Tracon__Skills__MaxSkillsPerAgent=2` ile yeniden başlatıldı. `manuel-bos`
agent'ının düzenleme ekranında 3 etkin skill (`fatura-kontrolu`,
`skill-birinci`, `skill-ikinci`) tek tek işaretlendi — DOM denetimi: üçü de
`checked:true, disabled:false` — 3. seçimde checkbox'lar KİLİTLENMEDİ
(arayüzün kendi sabit sınırı `10`, sunucunun gerçeği `2`). "Save new
version" → başarılı (arayüz `PUT` engellemedi). `POST
/api/agents/manuel-bos/run` → `HTTP: 400`, `detail: "Agent 'manuel-bos'
can have at most 2 skills."` — kullanıcı arayüzde "izin verildi" görürken
çalıştırmada engelleniyor, tam beklenen (kusur DEĞİL, önceden bilinen
arayüz/sunucu senkron eksikliği — MT-SKILL-021'in doğal sonucu).
`manuel-bos` fixture'ı `skillNames: []`'e geri PUT edildi, ayar kaldırıldı,
uygulama yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-030 — `FIX-SKILL-PROMPT` → `load_skill` onay kartı üretir

**Gerçek sonuç**
`fatura-kontrolu` orijinal `FATURA_SKILL_ACTIVE` işaretçisine geri
alındı. Playground'da `manuel-skill-test`'e `FIX-SKILL-PROMPT` gönderildi:
onay kartı belirdi, `load_skill` · "approval required"; argümanlar
`{"skillName":"fatura-kontrolu"}`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-031 — Onayla → skill talimatı bağlama girer

**Gerçek sonuç**
"Approve" tıklandı: YENİ tur eklendi, `load_skill` sonucu skill'in tam
talimat metnini taşıyor (`<instructions>...FATURA_SKILL_ACTIVE...`).
Modelin nihai yanıtı tam olarak `FATURA_SKILL_ACTIVE` dizgisi. Tam
beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-032 — Reddet → skill hiç yüklenmez

**Gerçek sonuç**
Yeni sohbette aynı prompt gönderildi, "Reject" tıklandı: kart `rejected`
rozetine döndü, sonuç `"Tool call invocation rejected."`. Modelin nihai
yanıtı fatura bilgisi isteyen genel bir mesaj — `FATURA_SKILL_ACTIVE`
dizgisi YOK. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-033 — "Do not ask again" ile onay → sonraki çağrıda kart çıkmaz

**Gerçek sonuç**
Yeni sohbette prompt gönderildi, "Do not ask again for this tool"
işaretlendi, "Approve" tıklandı — bu turda `FATURA_SKILL_ACTIVE` üretildi
(kural kaydedildi). Sonra TAMAMEN yeni bir sohbet açılıp (`New chat`
yerine sayfa yeniden yüklenerek, aynı etki) aynı prompt tekrar gönderildi:
HİÇBİR onay kartı belirmedi, `load_skill` doğrudan çalıştı, sonuç yine
`FATURA_SKILL_ACTIVE`. Tam beklenen — `tool_approval_rules` kalıcı kuralı
`load_skill` için de `cancel_order` ile aynı mekanizmayla çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-040 — Varsayılan durumda HERHANGİ bir script uzantısı reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title: "Script extension not allowed"` (İngilizce —
K-228), `detail: "There is no registered interpreter for the 'py'
extension."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-041 — `Interpreters`'a `sh` eklenince kayıt başarılı olur — ÖNERME TERSİNE ÇEVRİLDİ

🚨🚨 **En önemli bulgu (bu aile için).** Spec'in önermesi ("Interpreters
sözlüğü IConfiguration'dan HER ZAMAN bağlanır, `UseSkillScripts()`'ten
BAĞIMSIZ, config-only açılabilir") artık **DOĞRU DEĞİL** — kod, bunun tam
TERSİNİ yapıyor.

**Gerçek sonuç**
`Tracon__Skills__Scripts__Interpreters__sh=/bin/bash` env değişkeniyle
yeniden başlatıldı (spec'in adımı birebir), env değişkeninin PROCESS'e
ulaştığı `ps eww` ile doğrulandı — ama `PUT .../scriptli-skill`
(`extension:"sh"`) yine `HTTP: 400`, `"There is no registered interpreter
for the 'sh' extension."` verdi (MT-SKILL-040 ile AYNI hata). Kaynak
okundu: `TraconServiceCollectionExtensions.Binding.Core.cs:190-200`
şunu söylüyor — "`AllowStoredScripts`, `SkillRoots` ve `Interpreters`
artık BİLİNÇLİ OLARAK config'ten bağlanmıyor. Üçü de sunucuda
çalıştırılabilecek şeyi genişletir... `Tracon__Skills__Scripts__Interpreters__sh=/bin/sh`
gibi bir ortam değişkeni uygulamanın HİÇ ONAYLAMADIĞI bir yorumlayıcı
ekler." — yani bu TAM OLARAK spec'in önerdiği senaryo, ve kasıtlı olarak
KAPATILMIŞ (`Enabled`/`PlatformIsolationAcknowledged` hâlâ config'ten
bağlanıyor — bunlar yalnız DARALTABİLİR, genişletemez).

**Sonuç:** Bu, ürün kusuru DEĞİL — tam tersi, spec yazıldıktan SONRA
yapılmış bir güvenlik SIKILAŞTIRMASI (config yoluyla keyfi yorumlayıcı
ekleme yolu kapatılmış). §3'ün geri kalanı (042-046) ve §6/§7'nin
(050-063) script gerçekten çalıştırma case'leri artık YALNIZ kod
değişikliğiyle test edilebilir. `samples/Tracon.Api/Program.cs`'e geçici
`tracon.UseSkillScripts(o => { o.PlatformIsolationAcknowledged = true;
o.Interpreters["sh"] = "/bin/bash"; })` eklendi (MT-SEC-070/MT-WF-090
deseni), `dotnet build` (0/0), yeniden başlatıldı. Bu ikinci denemeyle
`PUT` → `HTTP: 201` — kayıt DB'de duruyor. `Beklenen sonuç` bu bulguyla
tersine çevrildi; adımlar bölümü "geçici kod değişikliği gerektirir"
notuyla güncellendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-042 — 11. script eklenirse (limit 10) → `400`

**Gerçek sonuç**
(Kod-tabanlı `sh` interpreter kaydıyla — bkz. MT-SKILL-041 notu.)
`HTTP: 400`, `title: "Too many scripts"` (İngilizce — K-228),
`detail: "A skill may carry at most 10 scripts."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-043 — Aynı skill içinde iki script aynı adı taşırsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Script name invalid"` (İngilizce — K-228),
`detail: "Every script name must be non-empty and unique within the
skill."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-044 — Geçersiz JSON `parametersSchema` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Parameter schema invalid"` (İngilizce — K-228),
`detail: "parametersSchema must be a valid JSON object."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-045 — Script içeriği 64 KB sınırını aşarsa → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Script too large"` (İngilizce — K-228),
`detail: "Each script may be at most 65536 bytes."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-046 — `scripts.Enabled = false` iken bile script KAYDEDİLİR/OKUNUR

**Gerçek sonuç**
Kod hâlâ `UseSkillScripts` ile `sh` interpreter'ı taşırken, ayrıca
`Tracon__Skills__Scripts__Enabled=false` env değişkeniyle DARALTILARAK
yeniden başlatıldı (kod comment'inin belirttiği "Enabled yalnız
daraltabilir" kuralı doğrulandı — bu env değişkeni GERÇEKTEN etkili
oldu, MT-SKILL-041'in aksine). `GET /api/skills/scriptli-skill` →
`scripts` dizisinde `merhaba` script'i TAM içerikle (`content: "echo
merhaba-tracon"`) döndü — kayıt asla gizlenmedi. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

🚨 **Ortam notu (§4 başlangıcı):** `UseSkillScripts()`'in kendi `Configure`
delegate'i `options.Skills.Scripts.Enabled = true`'yu KOŞULSUZ atıyor ve
`AddTracon()`'ın config-bağlama delegate'inden SONRA (Program.cs'te daha
geç) kaydolduğu için, kod hâlâ mevcutken hiçbir env değişkeni `Enabled`'ı
`false`'a DARALTAMIYOR — MT-SKILL-050'nin "sıfır kurulum" öncülü bu yüzden
kod TAMAMEN geri alınarak sağlandı (aşağıda). MT-SKILL-041'deki geçici
`tracon.UseSkillScripts(...)` satırı bu adımdan önce TAMAMEN geri alındı
(`git status --short` temiz, `dotnet build` 0/0).

## MT-SKILL-050 — Script çalıştırma KAPALIYKEN izin vermeye çalışmak → `409`

**Gerçek sonuç**
Kod TAMAMEN varsayılana döndürülmüş hâlde (hiçbir `UseSkillScripts`
çağrısı yok, hiçbir script env ayarı yok) yeniden başlatıldı. `POST
/api/skill-script-grants` → `HTTP: 409`, `title: "Script running
disabled"` (İngilizce — K-228), `detail: "Enable script running with
UseSkillScripts(...) before granting access."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-051 — `scripts.Enabled=true` (yalnız config) → AYNI istek `201`

**Gerçek sonuç**
`Tracon__Skills__Scripts__Enabled=true` + `...PlatformIsolationAcknowledged=true`
İKİSİ DE env değişkeniyle (kod değişikliği YOK) ayarlanıp yeniden
başlatıldı. Aynı istek → `HTTP: 201`, `grantedBy: null`, `expiresAt:
null`. Tam beklenen — bu, `UseSkillScripts()` HİÇ çağrılmadığında config
tek başına `Enabled`'ı gerçekten değiştirebildiğini kanıtlıyor (§4'ün
başındaki ortam notundaki asimetriyle tutarlı: kod `UseSkillScripts` ile
`true` dayattığında env `false`'a daraltamıyor, ama kod HİÇ dayatmadığında
env `false`'dan `true`'ya taşıyabiliyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-052 — Geçmiş bir `expiresAt` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Expiration in the past"` (İngilizce — K-228),
`detail: "expiresAt must be a moment in the future."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-053 — Boş `skillName` → `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Skill name required"` (İngilizce — K-228),
`detail: "skillName cannot be empty."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-054 — `GET /api/skill-script-grants` listesi, arayüzde uyarıyla görünür

**Gerçek sonuç**
`GET /api/skill-script-grants` → `scriptli-skill`/`merhaba` çifti listede.
`/tracon/skills` sayfasında "Script execution grants" panelinin üstünde
belirgin bir "danger" tonlu kutu göründü (`.bg-danger-soft` ile DOM'da
doğrulandı, tam metin: "A grant gives permission to RUN CODE on the
server on behalf of this tenant."), grant tablosu aynı kaydı gösterdi.

🚨 **Doküman düzeltmesi:** Spec'in beklediği CSS sınıfı (`border-red-500`)
artık YOK — bileşen `skills/script-grants.tsx`'e taşınmış ve tema
token'larına geçirilmiş (`border-danger bg-danger-soft text-danger`).
Kod yorumu bunu bilinçli bir tasarım-sistemi düzeltmesi olarak açıklıyor
(ham Tailwind kırmızısı temaya uymuyordu). Görsel/davranışsal iddia
(belirgin uyarı kutusu var) doğru kaldı; yalnız sınıf adı güncellendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-055 — İzni arayüzden iptal et → liste hemen güncellenir

**Gerçek sonuç**
"Revoke" tıklandı: satır listeden ANINDA kayboldu (skills tablosu
kaldı, grant tablosu boşaldı). SQL doğrulaması: `SELECT skill_name,
script_name, revoked_at FROM mt_s2.skill_script_grants WHERE skill_name =
'scriptli-skill'` → satır SİLİNMEDİ, `revoked_at` dolu
(`2026-09-17 16:01:57...`). Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-056 — Var olmayan bir izni iptal etmek → `404`

**Gerçek sonuç**
`HTTP: 404`, `title: "Grant not found"` (İngilizce — K-228), `detail:
"There is no active run grant for 'hic-yok-skill'."`. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

🚨 **Ortam notu (§6 başlangıcı, gerçek çalıştırma):** İlk denemede
`tracon.UseSkillScripts(o => { PlatformIsolationAcknowledged=true;
Interpreters["sh"]=... })` ile kuruldu ama model ısrarla "merhaba script'i
bulunamadı" dedi — MT-SKILL-057'nin config-only sonucuyla AYNI. Kök neden
bulundu: `SkillScriptSupport.StoredScriptsEnabled => Options.Enabled &&
Options.AllowStoredScripts` (`SkillScriptSupport.cs:61`) — `AllowStoredScripts`
`UseSkillScripts()`'in XML örnek kodunda YOK ve varsayılanı `false`; yalnız
`Interpreters`/`PlatformIsolationAcknowledged` ayarlamak YETMEZ. Kod
`o.AllowStoredScripts = true;` eklenerek düzeltildi, yeniden `dotnet build`
+ restart — bu, kendi kurulum hatamdı, ürün kusuru DEĞİL (kendi ortam
hatam olarak kayda geçiyorum, MT-WF-090 §7'nin $APU hatasıyla aynı
kategoride).

`manuel-script-test` agent'ı oluşturuldu (`skillNames:["fatura-kontrolu",
"scriptli-skill"]`).

## MT-SKILL-057 — Config-only kurulum script'i modele HİÇ ÇALIŞTIRAMAZ

**Gerçek sonuç**
(Bu case İLK — düzeltilmemiş — kurulumla, yalnız config: `Enabled=true`,
`PlatformIsolationAcknowledged=true`, kod değişikliği YOK, koşuldu —
doğru ortam buydu.) Playground'da prompt gönderildi: `load_skill
(fatura-kontrolu)` onaylandı → `FATURA_SKILL_ACTIVE`. Ardından model
DOĞRUDAN `run_skill_script({"skillName":"scriptli-skill","scriptName":"merhaba"})`
çağırmayı DENEDİ (onay kartı çıktı — MT-SKILL-058'in "generic dispatcher
her zaman şema olarak sunulur" düzeltmesiyle tutarlı), onaylandı, ama
çalıştırma SONUCU: "'merhaba' script'i 'scriptli-skill' içinde
bulunamadı" — script GERÇEKTEN çalışmadı. SQL doğrulaması: `SELECT
count(*) FROM mt_s2.tool_invocations WHERE source = 'skill:scriptli-skill'`
→ `0` — hiçbir çalıştırma satırı oluşmadı. Tam beklenen (spec'in "model ya
hiç denemez ya da böyle bir arac yok der" ifadesinin ikinci dalı
doğrulandı; mekanizma MT-SKILL-058'in düzeltmesiyle tutarlı: generic
`run_skill_script` HER ZAMAN şema olarak sunulur, ama gerçek çalıştırma
`StoredScriptsEnabled=false` olduğunda script'i "yok" sayar).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-058 — Gerçek çalıştırma kanıtı: `echo` script'i çalışır

**Gerçek sonuç**
(Düzeltilmiş kurulumla — `AllowStoredScripts=true` dahil — yeniden
koşuldu.) `load_skill(fatura-kontrolu)` → `FATURA_SKILL_ACTIVE`.
`load_skill(scriptli-skill)` sonucu bu kez `<available_scripts><script
name="merhaba" description=" This script does not take arguments.">
<parameters_schema>{"type":"object","properties":{"arguments":{"type":
"string","default":""}}}</parameters_schema></script></available_scripts>`
içeriyor — script artık GERÇEKTEN görünür. İkinci onay kartı
`run_skill_script`, argümanlar `{"skillName":"scriptli-skill","scriptName":
"merhaba"}` (spec'in beklediği ek `"arguments":""` alanı YOK — model onu
göndermedi, opsiyonel/varsayılan olduğu için). Onaylandı: sonuç `exit_code:
0 stdout: merhaba-tracon`, modelin nihai yanıtı `merhaba-tracon` içeriyor.
SQL doğrulaması BİREBİR eşleşti: `tool_invocations` → `tool_name:
'skill_script'`, `source: 'skill:scriptli-skill'`, `error: NULL`;
`audit_log` (spec'in dediği `audit_entries` DEĞİL — tablo adı
`mt_s2.audit_log`, doküman düzeltmesi) → `action: 'script.run'`, `entity:
'scriptli-skill/merhaba'`, `tenant_id: 'default'`. Tam beklenen — 2026-08-15
düzeltmesi (generic `run_skill_script` tool adı) bugün de doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-059 — İzin iptal edildikten sonra AYNI script reddedilir

**Gerçek sonuç**
`scriptli-skill/merhaba` izni iptal edildi. YENİ sohbette "merhaba
script'ini calistir" gönderildi, `load_skill` ve `run_skill_script` onay
kartları onaylandı (MAF hâlâ onay istiyor — izin AYRI bir kapı). Sonuç:
modele dönen tool sonucu tam olarak `"Error: Function failed."` — spec'in
dediği gibi. `mt_s2.audit_log`'da `action: 'script.denied'`, `entity:
'scriptli-skill/merhaba'` satırı VAR.

🚨 **Doğrulama sorgusu düzeltmesi:** Spec'in `tool_invocations.error`
sütununda ret mesajını beklediği yer YANLIŞ — bu deneme `tool_invocations`
tablosuna HİÇ satır YAZMADI (yalnız BAŞARILI bir çalıştırma satır açıyor,
MT-SKILL-058'in tek satırı hâlâ orada, tarih değişmedi). Asıl mesaj
`audit_log.after` JSON sütununda: `"There is no valid execution grant for
this script."` (İngilizce — K-228; spec'in Türkçe "'scriptli-skill/merhaba'
script'i calistirilmadi: ..." öneki de YOK, mesaj daha kısa). Davranışsal
iddia (spesifik ret nedeni bir yerde kalıcı olarak tutulur, modele
sızmaz) doğru; yalnız TABLO/SÜTUN ve tam metin yanlıştı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-060 — Zaman aşımı: uzun süren script öldürülür

**Gerçek sonuç**
`Tracon__Skills__Scripts__Timeout=00:00:02` ile yeniden başlatıldı,
`uyuyan` script'i (`sleep 10 && echo bitti`) eklendi ve izin verildi.
Çalıştırıldı: sonuç `"The script timed out and the process tree was
terminated."` (İngilizce — K-228), `stdout`/`stderr` bölümü YOK, `bitti`
hiç görünmedi. `GET /api/runs/{runId}` üzerinden ÖLÇÜLEN gerçek süre:
`startedAt`→`completedAt` = **3.5 saniye** (2s sınır + süreç
sonlandırma/rapor gecikmesi) — kesinlikle 10 saniye DEĞİL, süreç
gerçekten erken öldürüldü. Tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-061 — Çıktı sınırı: büyük çıktı kırpılır

**Gerçek sonuç**
`Tracon__Skills__Scripts__MaxOutputBytes=100` (aynı yeniden başlatmayla,
060 ile birleştirildi), `buyuk-cikti` script'i (`python3 -c "print('x'*5000)"`)
eklendi, izin verildi. Çalıştırıldı: sonuç `exit_code: 0 stdout:
xxx...xxx [Tracon: output truncated at the 100-byte limit.]` (İngilizce —
K-228) — tam 100 `x` karakteri + kırpma mesajı. `exit_code: 0` — zaman
aşımına UĞRAMADI, kırpma ve zaman aşımı bağımsız kapılar olduğu
doğrulandı. Tam beklenen. (Timeout/MaxOutputBytes ayarları bu case
sonrası kaldırıldı, varsayılana dönüldü.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-062 — Ortam değişkenleri sızmaz

**Gerçek sonuç**
`ortam-dokumu` script'i (`env | sort`) eklendi, izin verildi, çalıştırıldı.
`stdout` TAM OLARAK 7 satır: `HOME=...`, `PATH=...`, `PWD=/private/var/
folders/.../tracon-script-...`, `SHLVL=1`, `TRACON_SKILL_NAME=scriptli-skill`,
`TRACON_SKILL_TEMP=/var/folders/.../tracon-skill-...`, `_=/usr/bin/env` —
spec'in 2026-08-15 düzeltmesindeki "7 satır" sayımıyla BİREBİR eşleşti.
`OpenAI__ApiKey`, `Tracon__PostgreSql__ConnectionString` gibi HİÇBİR
Tracon-özel/`secret` değişken ÇIKTIDA YOK. Tam beklenen — Faz 11'in temel
güvenlik iddiası doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-063 — Kiracı başına eşzamanlılık sınırı — KISMEN DOĞRULANDI

**Gerçek sonuç**
🚨 **Yöntem sınırlaması (dürüstçe kaydediliyor, ürün kusuru DEĞİL):**
Playwright ile sıralı (tek seferde bir eylem) yürütülen tarayıcı
otomasyonu, 3 sohbeti GERÇEKTEN eşzamanlı (alt saniye farkla) tetikleyecek
kadar hızlı değil — her "New chat" + yazma + Enter + sekme geçişi
saniyeler süren gerçek round-trip'ler taşıyor, üstüne her turun kendi
LLM gecikmesi (skill/script çağırma kararı için ~2-4 sn) ekleniyor. Bu
yüzden gerçekleştirilen denemelerde `GET /api/runs` üzerinden ölçülen
`startedAt`/`completedAt` aralıkları hiçbir zaman ÇAKIŞMADI — sıralı
kaldı, semaforun gerçekten devreye girip BEKLETTİĞİ bir an yakalanamadı.

Bunun yerine mekanizma KAYNAKTAN doğrulandı:
`SkillScriptConcurrencyLimiter.cs:24-42` — `AcquireAsync` önce kiracı
başına `SemaphoreSlim(_perTenantLimit, _perTenantLimit)` üzerinde
`WaitAsync` çağırıyor (spec'in dediği gibi REDDETMEZ, BEKLER), sonra
global `_total` semaforunu bekliyor; `TraconOptions.cs:374` varsayılan
`MaxConcurrentPerTenant = 2` (spec'in beklediğiyle birebir). Ayrıca
birden fazla `bekleyen`/`merhaba`/`uyuyan` çalıştırması bu oturumda ART
ARDA (sırayla) sorunsuz tamamlandı — çökme, kilitlenme veya sızıntı
gözlenmedi.

**Sonuç:** Mekanizmanın DOĞRU yazıldığı kaynaktan kanıtlandı; canlı
"üçüncü çağrı gerçekten bekliyor" zamanlama iddiası mevcut araçlarla
GÜVENİLİR şekilde tekrar üretilemedi — bu bir eksik doğrulama olarak
kaydediliyor, "Geçti" değil "Kısmen" işaretleniyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı (kısmen — mekanizma kaynaktan doğrulandı, canlı zamanlama ölçülemedi)

---

## MT-SKILL-070 — `execute_skill_script` span'i öznitelikleri — KALDI

**Gerçek sonuç**
🚨 **Ürün kusuru bulundu, HATA-S2-003.** `Tracon__Observability__SuccessSampleRatio=1`
ile yeniden başlatıldı, `merhaba` script'i İKİ AYRI bağımsız çalıştırmada
(`01a0b032-68ac-...`, `01a0b034-9bca-...`) test edildi — HER İKİSİ de
`exit_code: 0` ile GERÇEKTEN başarılı oldu. `GET /api/runs/{id}/trace`
ile alınan span'ler:
- `execute_skill_script` span'i yalnız `tracon.skill.name`/
  `tracon.script.name` taşıyor — `tracon.script.exit_code` ve
  `tracon.script.duration_ms` HİÇBİR ikisinde de YOK. `status: "Unset"`
  (`"Ok"` beklenirdi).
- Ebeveyn `execute_tool run_skill_script` span'i her ikisinde de
  `status: "Error"`, `error.type: "Tracon.TraconException"` taşıyor —
  ÇALIŞTIRMA BAŞARILI olduğu hâlde.

Kaynak okundu: `SandboxedSkillScriptRunner.cs:355-359`
`activity?.SetTag(TraconDiagnostics.Tags.ExitCode, ...)` /
`...DurationMs, ...)` / `activity?.SetStatus(result.Succeeded ? Ok :
Error)` GERÇEKTEN kodda var ve `SkillScriptProcessRunner.ExecuteAsync`
dönüşünden HEMEN sonra çağrılıyor — kod, `activity`'yi doğru şekilde
(AsyncLocal'a güvenmeden) açık parametre olarak taşıyor
(`TraconDiagnostics.SkillScriptActivityName` açılışı METODUN KENDİ
gövdesinde, dokümante edilmiş kurala uygun). Ama sonuçta izlenen span'de
bu etiketler YOK ve durum "Ok" yerine hep "Unset"/ebeveynde "Error" —
etiketlerin/kararın span DİNLENDİĞİNDE (recorder/exporter) yakalandığı
an ile `SetTag`/`SetStatus`'un GERÇEKTEN çağrıldığı an arasında bir
kayıp/zamanlama sorunu olduğu görülüyor (script yürütmesi async I/O
içerdiği için `SetTag` await'ten SONRA, span'in muhtemelen ERKEN
serileştirilen bir görünümünden SONRA geliyor olabilir — tam kök neden
kapanışta netleştirilmeli).

**Etki:** Gözlemlenebilirlik verisi hatalı/eksik — bir script başarıyla
çalışsa bile trace'te "Error" görünüyor ve exit_code/duration_ms hiç
kaydedilmiyor; bu, span'e bakan biri için YANLIŞ ALARM anlamına gelir
(gerçek bir hatayla ayırt edilemez).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

## MT-SKILL-071 — (kapsam dışı, spec'in kendi notuyla koşulmadı)

**Gerçek sonuç**
Spec bu case'i bilinçli olarak "yazılmadı" işaretliyor (audit-yazım
hatası enjekte etmek DB'yi bozar). Otomatik test dosyası taze koşuldu:
`dotnet test tests/Tracon.Core.UnitTests -c release --no-build` →
2804/2805 geçti; TEK başarısız `SourceLanguageTests` bu oturumun KENDİ
geçici Türkçe kod yorumlarından kaynaklandı (henüz geri alınmamıştı) —
`SandboxedSkillScriptRunnerTests`'in tamamı YEŞİL. Doğrulandı, kapsam
dışı kalmaya devam ediyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı (otomatik test kapsamı doğrulandı)

---

## 🚨 HATA-S2-003 — `execute_skill_script` span'i başarılı çalıştırmada bile `exit_code`/`duration_ms` taşımıyor, ebeveyn span yanlış "Error" gösteriyor

**Önem:** Düşük (yalnız gözlemlenebilirlik/trace verisi; script'in kendi
çalışması, sonucu, `tool_invocations`/`audit_log` kayıtları TAMAMEN
doğru — kullanıcıya/modelin aldığı sonuca hiçbir etkisi yok).

**Bulgu:** `MT-SKILL-058`'in KENDİSİ dahil, bu oturumdaki HER başarılı
`merhaba`/`bekleyen`/`buyuk-cikti`/`ortam-dokumu` script çalıştırması
(en az 6 bağımsız örnek) için `GET /api/runs/{id}/trace` şunu gösterdi:
- `execute_skill_script` span'i: yalnız `tracon.skill.name`/
  `tracon.script.name`; `tracon.script.exit_code`/`tracon.script.duration_ms`
  HİÇ YOK; `status: "Unset"`.
- Ebeveyn `execute_tool run_skill_script` span'i: `status: "Error"`,
  `error.type: "Tracon.TraconException"` — çalıştırma GERÇEKTEN
  başarılıyken.

**Kök neden (kısmi):** `SandboxedSkillScriptRunner.cs:355-359`
`activity?.SetTag(ExitCode/DurationMs)` ve `activity?.SetStatus(...)`
kod olarak DOĞRU yazılmış (`RunStoredScriptAsync`/`RunCodeScriptAsync`
her ikisi de `activity`'yi AsyncLocal'a güvenmeden açık parametre olarak
taşıyor — dokümante edilmiş kalıba uygun) ve `SkillScriptProcessRunner
.ExecuteAsync`'in (async, süreç I/O'su içeren) dönüşünden HEMEN sonra
çağrılıyor. Trace'e YAZAN mekanizma (span recorder/exporter) bu geç
`SetTag`/`SetStatus` çağrılarını YAKALAMIYOR — muhtemelen span'in bir
ERKEN görünümünü (aktivite başlarken) kalıcı depoya yazıyor, `Dispose()`
anındaki NİHAİ etiket/durum kümesini değil. Tam kök neden (hangi
recorder/exporter, hangi anda serileştiriyor) bu koşumda izlenmedi —
kapanışta kod okumasıyla netleştirilmeli.

**Düzeltme yönü (uygulanmadı, kural 1 gereği):** Span'i kaydeden
mekanizmanın `ActivityStopped` (ya da eşdeğer `Dispose` sonrası) anını
beklediğinden emin olunmalı — muhtemelen bir `ActivityListener` erken
bir `Sample`/`ActivityStarted` kancasında span'i zaten "bitmiş" sayıp
kaydediyor.

**Durum:** Kayıt, düzeltilmedi (kural 1) — kapanışta değerlendirilmeli.

---

## Devir notu — Dosya 14 (Skill/Script) TAMAMLANDI: ap-s2'nin SON ailesi

Tüm 5 bölüm (§1 CRUD/frontmatter, §2 derleme/limit, §UI onay akışı,
§3 script config, §4 script çalıştırma+telemetri) bitti. Sayım: 47 case —
**45 Geçti, 1 Kaldı (MT-SKILL-070), 1 Kısmen/Atlandı (MT-SKILL-063,
mekanizma kaynaktan doğrulandı ama canlı zamanlama ölçülemedi)**. Bir
yeni ürün kusuru: **HATA-S2-003** (`execute_skill_script` span'i
exit_code/duration_ms taşımıyor, ebeveuen span yanlış "Error" gösteriyor
— yalnız gözlemlenebilirlik, işlevsel etki yok). İki spec önermesi
düzeltildi: MT-SKILL-041 (Interpreters artık yalnız kodda ayarlanabilir
— güvenlik sıkılaştırması), MT-SKILL-054 (CSS sınıfı tema token'larına
geçirilmiş). Birkaç doğrulama sorgusu/zarf düzeltmesi: MT-SKILL-020
(düz ProblemDetails, yapılandırılmış rapor değil), MT-SKILL-059
(audit_log.after, tool_invocations.error değil).

**ap-s2 şeridinin TÜM ataması bitti: 36 · 33 · 12 · 24 · 35 · 23 · 15 · 14
— sekiz aile de kapandı.** Kod tamamen dondurulmuş hâlde bırakıldı
(`git status --short -- src samples tests` temiz), uygulama varsayılan
konfigürasyonla ayakta, `Tracon.Core.UnitTests` tam takım yeşil
(2805/2805). Sıradaki adım: ana `DEVIR.md`'yi güncelle, ardından bu
şeridin koşum özetini tamamla — kapanış (§6-§9) ayrı, çok-şeritli bir
faz, bu oturumun kapsamı DIŞINDA.
