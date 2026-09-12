# 14 — Skill ve Script Çalıştırma (`SKILL`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../14-SKILL-VE-SCRIPT.md`](../../14-SKILL-VE-SCRIPT.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/14-SKILL-VE-SCRIPT.md
> ```

---

## Temiz geçen case'ler (36)

| Case | Durum | Başlık |
|---|---|---|
| MT-SKILL-001 | ☑ | `PUT /api/skills/{name}` yeni bir skill oluşturur (`201`) |
| MT-SKILL-002 | ☑ | Aynı skill'i tekrar `PUT` etmek günceller (`200`), `version` artar |
| MT-SKILL-003 | ☑ | `GET /api/skills` kiracının skill listesini döner |
| MT-SKILL-004 | ☑ | `DELETE` skill'i ve cascade kaynaklarını siler |
| MT-SKILL-005 | ☑ | Var olmayan skill'i silmek → `404` |
| MT-SKILL-006 | ☑ | Yoldaki ad ile gövdedeki ad uyuşmazsa → `400` |
| MT-SKILL-007 | ☑ | Büyük harf/alt çizgi içeren ad → `400` (MAF'ın kendi ad kuralı) |
| MT-SKILL-008 | ☑ | 65 karakterlik ad (64 sınırını aşan) → `400` |
| MT-SKILL-009 | ☑ | Boş `description` → `400` |
| MT-SKILL-010 | ☑ | `instructions` 64 KB sınırını aşarsa → `400` |
| MT-SKILL-011 | ☑ | 21. kaynak eklenirse (limit 20) → `400` |
| MT-SKILL-012 | ☑ | Aynı skill içinde iki kaynak aynı adı taşırsa → `400` |
| MT-SKILL-013 | ☑ | Arayüzden skill oluşturma ve düzenleme (Skills ekranı) |
| MT-SKILL-022 | ☑ | Devre dışı skill derlemeye girmez, model `load_skill` içinde hiç görmez |
| MT-SKILL-023 | ☑ | Kod tanımlı skill, aynı adlı DB kaydını geçersiz kılar |
| MT-SKILL-024 | ☑ | Skill düzenlemesi, `CompiledAgentCache` parmak izini değiştirir |
| MT-SKILL-025 | ☑ | Sunucu `MaxSkillsPerAgent`'ı değiştirir, arayüz checkbox limiti HABERSİZ kalır |
| MT-SKILL-030 | ☑ | `FIX-SKILL-PROMPT` → `load_skill` onay kartı üretir |
| MT-SKILL-031 | ☑ | Onayla → skill talimatı bağlama girer, model işaretçiyi birebir üretir |
| MT-SKILL-033 | ☑ | "Hatırla" ile onay → sonraki çağrıda `load_skill` kartı hiç çıkmaz |
| MT-SKILL-040 | ☑ | Varsayılan durumda (Interpreters boş) HERHANGİ bir script uzantısı reddedilir |
| MT-SKILL-041 | ☑ | `Interpreters`'a `sh` eklenince AYNI kayıt başarılı olur (yalnız config, kod değişikliği YOK) |
| MT-SKILL-042 | ☑ | 11. script eklenirse (limit 10) → `400` |
| MT-SKILL-043 | ☑ | Aynı skill içinde iki script aynı adı taşırsa → `400` |
| MT-SKILL-044 | ☑ | Geçersiz JSON `parametersSchema` → `400` |
| MT-SKILL-045 | ☑ | Script içeriği 64 KB sınırını aşarsa → `400` |
| MT-SKILL-046 | ☑ | `scripts.Enabled = false` iken bile bir script KAYDEDİLİR ve GERİ OKUNUR |
| MT-SKILL-050 | ☑ | Script çalıştırma KAPALIYKEN izin vermeye çalışmak → `409` |
| MT-SKILL-051 | ☑ | `scripts.Enabled = true` yapılınca (yalnız config) AYNI istek `201` döner |
| MT-SKILL-052 | ☑ | Geçmiş bir `expiresAt` → `400` |
| MT-SKILL-053 | ☑ | Boş `skillName` → `400` |
| MT-SKILL-054 | ☑ | `GET /api/skill-script-grants` listesi, arayüzde kırmızı uyarıyla görünür |
| MT-SKILL-055 | ☑ | İzni arayüzden iptal et (`Revoke`) → liste hemen güncellenir |
| MT-SKILL-056 | ☑ | Var olmayan bir izni iptal etmek → `404` |
| MT-SKILL-062 | ☑ | Ortam değişkenleri sızmaz: script yalnız `PATH`/`HOME` (+2 enjekte edilen) görür |
| MT-SKILL-063 | ☑ | Kiracı başına eşzamanlılık sınırı: 3. eşzamanlı çağrı ilk ikisi bitene kadar BEKLER |

## Ayrıntı taşıyan case'ler (10)

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
Adım 1: `HTTP: 201`. Adım 2: `HTTP: 400`, `title: "Agent derlenemedi"`, `detail: "'iki-skilli-agent' agent'i en fazla 1 skill tasiyabilir."`. Tam beklendiği gibi — koşumun ilk denemesinde eski uygulama süreci `pkill` deseniyle yakalanamadığı için (apphost ikili adı `Tracon.Api`, `dotnet ... .dll` değil) yeniden başlama sessizce başarısız oldu ve `MaxSkillsPerAgent` hiç uygulanmadı (adım 2 yanlışlıkla `200` döndü); PID ile `kill -9` edilip doğru ortam değişkenleriyle yeniden başlatıldıktan sonra tekrarlanan koşum yukarıdaki sonucu verdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** `dotnet user-secrets remove "Tracon:Skills:MaxSkillsPerAgent"`.

---

## MT-SKILL-032 — Reddet → skill hiç yüklenmez, model onsuz devam eder

**Gerçek sonuç**
"Reddet"e tıklandı: kart `rejected` rozetine döndü, `load_skill` sonucu `Tool call invocation rejected.` oldu. Belgelenmeyen bir nüans: model, reddedilen çağrıyı bir kez daha denedi ve İKİNCİ bir `load_skill` onay kartı üretti (gpt-5.4-mini'nin retry davranışı — kod tarafında bir tekrar mekanizması değil, modelin kendi kararı); bu da reddedildi, ardından tur tamamlandı. Nihai yanıt genel bir "hangi fatura bilgilerini paylaşmalısın" metniydi, `FATURA_SKILL_ACTIVE` dizgisini İÇERMİYORDU. Asıl iddia (skill talimatı hiçbir zaman bağlama girmedi) doğrulandı; ekstra onay turu kusur değil, gerçek model davranışı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-057 — 🚨 Config-only kurulum (kod değişikliği OLMADAN) script'i modele HİÇ sunmaz

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-SKILL-058 — Gerçek çalıştırma kanıtı: `echo` script'i onaydan geçer ve modele sonucu döner

**Gerçek sonuç**
🚨 **KRİTİK KUSUR.** §6'nın ortak ön koşulu (geçici `tracon.UseSkillScripts(o => o.PlatformIsolationAcknowledged = true);` kod satırı + `AllowStoredScripts=true`) uygulanıp yeniden derlendikten/başlatıldıktan sonra, `scriptli-skill`'e bağlı (script içeren, gerçekten etkin) HERHANGİ bir agent'a gönderilen HER istek `run` başlarken şu hatayla çöküyor: `InvalidOperationException: JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only.` `load_skill` onay kartı hiç çıkmıyor, model hiç çağrılmıyor — hata skill'in MAF'a sunulacağı derleme anında oluşuyor. Hem Playground'dan (`manuel-script-test`, "Fatura kontrol..." promptu) hem doğrudan `POST /api/agents/manuel-script-test/run` ile ("merhaba" gövdesi) doğrulandı, ikisi de aynı hatayı üretti — tam belirlenimli (deterministik), model içeriğinden bağımsız.

**Kök neden (kod okunarak doğrulandı):** `src/Tracon.Core/Skills/TraconSkillsSource.cs:10` — `private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);` — resolver'ı hiç ayarlanmamış, salt reflection tabanlı bir `JsonSerializerOptions` örneği. Bu örnek `_scripts is { StoredScriptsEnabled: true }` iken (`TraconSkillsSource.cs:71-81`) MAF'ın `skill.AddScript(script.Name, delegate, description, SerializerOptions)` çağrısına aynen geçiriliyor; MAF bu seçenekler nesnesini içeride `MakeReadOnly()` ile donduruyor (resolver popüle edilmeden), bu da .NET'in "TypeInfoResolver olmadan salt-okunur işaretlenemez" korumasını tetikliyor. `StoredScriptsEnabled: false` iken (§4/§5, MT-SKILL-057) bu kod yolu (`skill.AddScript`) hiç çağrılmadığı için sorun gizli kalıyor — bu yüzden script KAYDI/İZNİ katmanındaki 14 case (§4+§5) sorunsuz geçti ama gerçek ÇALIŞTIRMA katmanının TAMAMI (§6) bu satırda çöküyor.

**Kapsam:** Bu, script çalıştırma özelliğinin (Faz 11) yayınlanan hâlde TAMAMEN işlevsiz olduğu anlamına gelir — `UseSkillScripts()` çağıran ve saklı script'i olan HER tüketici aynı çökmeyi yaşar. MT-SKILL-059..063 ve 070 AYNI kök nedenle bloklanıyor (script gerçekten çalıştırılmadan hiçbiri gözlemlenemez); kullanıcı kararıyla bu case'ler tek tek tekrar denenmeden "aynı kök nedenle Kaldı" olarak işaretlendi, ayrıntı için bu case'e bakınız.

**🔧 Kapanış güncellemesi (2026-08-14):** Kök neden HATA-K-002 olarak kodlandı ve düzeltildi (K-400, `SONUCLAR-K-2026-08-13.md`) — bu case'in kendisi yeniden koşulup uçtan uca doğrulandı (yukarıdaki Durum satırına bakınız). MT-SKILL-059..063 ve 070 henüz TEK TEK yeniden koşulmadı (bu düzeltme oturumunun kapsamı "çöküşü gider + doğrula", "her bloklu case'i tekrar koş" değildi) — engel artık kalkmış durumda, bu case'ler gelecek bir koşumda normal şekilde tekrar denenebilir.

**🚨 Kapanış güncellemesi (2026-08-15, KAPANIS-PLANI §9/§6 Aile W) — MT-SKILL-059..070'in gerçek yeniden koşumu İKİ YENİ ve BAĞIMSIZ kritik kusur buldu.** K-400 kök nedeni gerçekten kapalı (bu case'in kendi "geçti" iddiası yukarıdaki 2026-08-14 notuyla tutarlı — `load_skill` onayı sorunsuz geçiyor), ama script'in GERÇEKTEN çalıştırılması hâlâ iki farklı yerde çöküyordu; bu case'in 2026-08-14 koşumu bunları YAKALAMADI çünkü `echo merhaba-tracon` gibi stdin okumayan bir script'in zamanlamasına şans eseri denk gelmedi (ırk koşulu — deterministik değil, aşağıya bakınız). 2026-08-15'te tam ortam kurulup (§6 ortak ön koşulu) MT-SKILL-059..070'in HEPSİ canlı OpenAI çağrısıyla gerçekten koşulunca:

1. **`SkillScriptProcessRunner.WriteArgumentsAsync`'in `finally` bloğu** (`process.StandardInput.Close()`) `try/catch`'in DIŞINDAydı; stdin'i hiç okumadan çıkan (`echo` gibi) bir script'te `Close()`'un kendi iç flush'ı `IOException: Pipe is broken` fırlatıyor ve bu YAKALANMADAN dışarı sızıyordu — MAF'ın `run_skill_script` çağrısı `"Error: Function failed."` ile başarısız oluyordu. **Script çalıştırma özelliğinin TAMAMI (K-400 kapandıktan SONRA bile) fiilen işlevsizdi.**
2. **`SandboxedSkillScriptRunner.DenyAsync`** red nedenini (düz metin) `jsonb` sütununa JSON'a çevirmeden yazıyordu; her `script.denied` denetim izi `22P02 invalid input syntax for type json` ile sessizce kayboluyordu (Faz 9'un "gözlemlenebilirlik hatası çalıştırmayı bozmaz" bilinçli istisnası devreye giriyordu — kayıt kaybolsa da red işliyordu, ama denetim izi HİÇ oluşmuyordu).

İkisi de bu koşumda düzeltildi (bkz. `docs/hafiza/cekirdek-calistirma.md`, HATA-K-skill-pipe / HATA-K-skill-audit-json), regresyon testleriyle kilitlendi
(`SkillScriptProcessRunnerTests.Stdin_okumadan_cikan_script_boru_kirik_istisnasi_firlatmaz`,
`SandboxedSkillScriptRunnerTests.Izin_yokken_script_reddedilir_ve_denetim_izine_yazilir`'e
JSON geçerlilik denetimi eklendi), ardından MT-SKILL-058..070'in TAMAMI canlı
OpenAI ile yeniden koşulup doğrulandı — ayrıntı §6 Aile W.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — 2026-08-15'te üçüncü kez uçtan uca koşuldu (iki yeni kusur düzeltildikten sonra): `load_skill` onayı → `run_skill_script(scriptName=merhaba)` onayı → gerçek çalıştırma → `exit_code: 0\nstdout:\nmerhaba-tracon\n\n`, `tool_invocations.error IS NULL`, `audit_log`'da `script.run`/`scriptli-skill/merhaba`. Tool adı beklentisi yukarıda koda göre düzeltildi.

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
   `JsonSerializer.Serialize(reason, TraconCoreJsonContext.Default.String)`.
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

**Temizlik:** `dotnet user-secrets remove "Tracon:Skills:Scripts:Timeout"` — uygulandı.

---

## MT-SKILL-061 — Çıktı sınırı: büyük çıktı kırpılır, kırpma mesajı eklenir

**Gerçek sonuç**
_(2026-08-13/14 koşumları: Kaldı — MT-SKILL-058'in kök nedeniyle bloklu.)_

**2026-08-15 gerçek koşum (KAPANIS-PLANI §9) — Geçti.** `load_skill` →
`run_skill_script(scriptName=buyuk-cikti)` onaylandı; sonuç birebir
beklenen: `exit_code: 0`, `stdout` tam 100 `x` karakterine kırpıldı, ardından
`\n[Tracon: cikti 100 bayt sinirinda kirpildi.]` metni. Zaman aşımına
UĞRAMADI. (İlk denemede script içeriği bu koşumun kendi kayıt scriptindeki
bir tırnak-kaçışı hatasıyla `print(x * 5000)` olarak kaydedilmişti — Python
`NameError` üretti, ama kırpma mesajı yine de doğru tetiklendi; içerik
düzeltilip temiz bir `stdout` ile tekrarlandı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Temizlik:** `dotnet user-secrets remove "Tracon:Skills:Scripts:MaxOutputBytes"` — uygulandı.

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
  'tracon.skill.name': 'scriptli-skill',
  'tracon.script.name': 'merhaba',
  'tracon.script.exit_code': '0',
  'tracon.script.duration_ms': '66.4933'
}
```
Aynı iz, model tarafına sunulan tool listesini de doğruladı —
`gen_ai.tool.definitions` yalnız `load_skill`/`read_skill_resource`/
`run_skill_script` içeriyor (`merhaba` diye ayrı bir tool adı YOK) — bu,
`MT-SKILL-058`'in tool-adı düzeltmesinin bağımsız bir doğrulamasıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
