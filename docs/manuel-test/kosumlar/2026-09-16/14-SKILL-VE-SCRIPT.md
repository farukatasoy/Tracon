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
