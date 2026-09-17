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
