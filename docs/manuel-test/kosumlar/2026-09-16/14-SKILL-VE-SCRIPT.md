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

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 89f44ab3:docs/manuel-test/kosumlar/2026-09-16/14-SKILL-VE-SCRIPT.md
> ```

---

## Temiz geçen case'ler (33)

| Case | Durum | Başlık |
|---|---|---|
| MT-SKILL-001 | ☑ | `PUT /api/skills/{name}` yeni bir skill oluşturur (`201`) |
| MT-SKILL-002 | ☑ | Aynı skill'i tekrar `PUT` etmek günceller (`200`) |
| MT-SKILL-003 | ☑ | `GET /api/skills` kiracının skill listesini döner |
| MT-SKILL-004 | ☑ | `DELETE` skill'i ve cascade kaynaklarını siler |
| MT-SKILL-005 | ☑ | Var olmayan skill'i silmek → `404` |
| MT-SKILL-006 | ☑ | Yoldaki ad ile gövdedeki ad uyuşmazsa → `400` |
| MT-SKILL-007 | ☑ | Büyük harf/alt çizgi içeren ad → `400` |
| MT-SKILL-008 | ☑ | 65 karakterlik ad → `400` |
| MT-SKILL-009 | ☑ | Boş `description` → `400` |
| MT-SKILL-010 | ☑ | `instructions` 64 KB sınırını aşarsa → `400` |
| MT-SKILL-011 | ☑ | 21. kaynak eklenirse (limit 20) → `400` |
| MT-SKILL-012 | ☑ | Aynı skill içinde iki kaynak aynı adı taşırsa → `400` |
| MT-SKILL-014 | ☑ | Skill'i arayüzden devre dışı bırakma, checkbox kilitlenir |
| MT-SKILL-021 | ☑ | `MaxSkillsPerAgent` aşımı SAVE'de geçer, yalnız RUN'da `400` |
| MT-SKILL-022 | ☑ | Devre dışı skill derlemeye girmez, model `load_skill` görmez |
| MT-SKILL-023 | ☑ | Kod tanımlı skill, aynı adlı DB kaydını geçersiz kılar |
| MT-SKILL-024 | ☑ | Skill düzenlemesi, önbellek parmak izini değiştirir |
| MT-SKILL-030 | ☑ | `FIX-SKILL-PROMPT` → `load_skill` onay kartı üretir |
| MT-SKILL-031 | ☑ | Onayla → skill talimatı bağlama girer |
| MT-SKILL-032 | ☑ | Reddet → skill hiç yüklenmez |
| MT-SKILL-033 | ☑ | "Do not ask again" ile onay → sonraki çağrıda kart çıkmaz |
| MT-SKILL-040 | ☑ | Varsayılan durumda HERHANGİ bir script uzantısı reddedilir |
| MT-SKILL-042 | ☑ | 11. script eklenirse (limit 10) → `400` |
| MT-SKILL-043 | ☑ | Aynı skill içinde iki script aynı adı taşırsa → `400` |
| MT-SKILL-044 | ☑ | Geçersiz JSON `parametersSchema` → `400` |
| MT-SKILL-045 | ☑ | Script içeriği 64 KB sınırını aşarsa → `400` |
| MT-SKILL-050 | ☑ | Script çalıştırma KAPALIYKEN izin vermeye çalışmak → `409` |
| MT-SKILL-051 | ☑ | `scripts.Enabled=true` (yalnız config) → AYNI istek `201` |
| MT-SKILL-052 | ☑ | Geçmiş bir `expiresAt` → `400` |
| MT-SKILL-053 | ☑ | Boş `skillName` → `400` |
| MT-SKILL-055 | ☑ | İzni arayüzden iptal et → liste hemen güncellenir |
| MT-SKILL-060 | ☑ | Zaman aşımı: uzun süren script öldürülür |
| MT-SKILL-061 | ☑ | Çıktı sınırı: büyük çıktı kırpılır |

## Ayrıntı taşıyan case'ler (14)

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

**Gerçek sonuç — kapanış ölçümü (2026-09-19) — KISMİ, case `Kaldı` KALIYOR**
`HATA-S2-003`'ün kaydındaki teşhis (recorder span'i erken serileştiriyor)
**ölçülerek çürütüldü**, ama case'in kendi senaryosu bu oturumda birebir
yeniden üretilemedi.

**Ölçülen:**
1. Gerçek bir `ActivityListener` ve gerçek bir süreç çalıştırmasıyla:
   BAŞARILI bir koşum span'e `tracon.script.exit_code = 0`,
   `tracon.script.duration_ms` ve `status = Ok` **bırakıyor**
   (`A_successful_run_leaves_its_exit_code_and_duration_on_the_span`).
2. `RunTraceCollector` etiketleri `ActivityStopped` anında okuyor ve
   duyarlı olmayan her etiketi saklıyor; iki etiket adı da filtresinden
   geçiyor (`IsSensitive` yalnız `message`/`prompt`/`completion` arar).

∴ Yalnız iki ad etiketiyle biten bir span, etiket satırına **hiç
ulaşmamış** bir koşumdur; metottan o satırdan önce çıkan tek yol bir
kapının `TraconException` fırlatmasıdır — kaydın ebeveyn span'de gördüğü
`error.type: Tracon.TraconException` ile birebir uyuşan şey budur.

**Düzeltme:** kapanan her kapı artık span'in durumunu `Error` yapıyor ve
kendini `tracon.script.denial_reason` ile adlandırıyor
(`A_denied_run_leaves_the_gate_that_stopped_it_on_the_span`, düzeltme
öncesi kırmızı olduğu doğrulandı). Aynı iz artık kendini açıklıyor.

**Açık kalan:** kayıt aynı çalıştırmaların `exit_code: 0` ile başarılı
olduğunu da söylüyor. Kod yoluna göre bu ikisi tek bir çağrının doğrusu
olamaz; en olası okuma iki AYRI çağrıya bakıldığıdır (iz düşen çağrıyı,
`tool_invocations`/`audit_log` başarılı olanı gösterir). Bunu kanıtlamak
o oturumun skill/script/grant fixture'ını canlı sunucuda yeniden kurmayı
gerektirir; bu oturumda kurulmadı. Case bu yüzden `Kaldı` kalıyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı — `HATA-S2-003`
(kısmen kapandı: reddedilen koşumun izi düzeltildi ve kilitlendi; başarılı
koşumun kayıttaki semptomu yeniden üretilemedi)

---

**Yeniden koşum — 2026-09-19 (kapanış, fixture canlı kuruldu) · ☑ GEÇTİ**

Kapanışın "açık kalan" maddesi **ölçüldü ve kapandı.** `samples/Tracon.Api`'ye
geçici `UseSkillScripts` eklendi (ölçüm sonrası **geri alındı**), `mt_z70`
şeması, `SuccessSampleRatio=1`, gerçek OpenAI. Skill + script + grant + agent
canlı kuruldu ve `merhaba` script'i onay kartlarından geçirilerek **gerçekten**
çalıştırıldı:

```
functionResult: "exit_code: 0\nstdout:\nmerhaba-tracon\n\n"
```

`GET /api/runs/01a0b720-0cb6-73b2-8c08-ce3eb89ed142/trace`:

```
--- execute_skill_script | status = Ok
     tracon.script.duration_ms = 53.0284
     tracon.script.exit_code   = 0
     tracon.script.name        = merhaba
     tracon.skill.name         = scriptli-skill
--- execute_tool run_skill_script | status = Unset     <- "Error" DEGIL
```

**Dört beklentinin dördü de karşılandı** ve turun iki semptomu da gitti:
`exit_code`/`duration_ms` **var**, span durumu `Unset` değil **`Ok`**, ebeveyn
span `Error` **değil**.

🚨 **Kapanışın hipotezi doğrulandı.** 2026-09-19'un kısmi kapanışı "kayıt iki
AYRI çağrıya bakmış olmalı; iki ad etiketiyle biten span, etiket satırına hiç
ulaşmamış — yani bir kapının `TraconException` fırlattığı **reddedilen** bir
koşumdur" diyordu. Başarılı koşum bugün ölçüldü ve **tam da beklendiği gibi**
dört etiketi ve `Ok`'ı bıraktı. `HATA-S2-003` artık **tamamen** kapalıdır.

⚠️ **Spec'in §6 kod parçası eksikti ve düzeltildi** (skill §1.1 istisnası).
`UseSkillScripts` çağrısı yalnız `PlatformIsolationAcknowledged` ve
`Interpreters` ayarlıyordu; **`AllowStoredScripts = true` yok**. O bayrak
olmadan `TraconSkillsSource.CreateSkill` (`TraconSkillsSource.cs:81`) depodaki
script'leri modele **hiç sunmuyor** ve `run_skill_script` `Error: Script
'merhaba' not found in skill 'scriptli-skill'.` döndürüyor — kayıt DB'de
dururken. Belirti yanıltıcıdır: skill API'si script'i gösterir, model göremez.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
