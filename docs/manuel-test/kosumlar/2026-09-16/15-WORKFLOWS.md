# 15 — Workflows — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum 15, ap-s2 devam):** §1 (MT-WF-001..020) bitti —
> **20/20 Geçti, 0 Kaldı**. Ortam: ap-s2'nin kendi PostgreSQL örneği (port
> 5082, `mt_s2` şeması) — bu aile SQLite gerektirmiyor (SQL doğrulaması
> zaten PostgreSQL varsayıyor), doğrudan kullanıldı. Sırada: §UI (030-035,
> Playwright, 6 case), §2 (040-044), §3 resume (050-053), §4 HITL boolean
> (060-066), §5 Magentic plan (070-073), §6 graph (080-084), §7
> config/limits (090-097), §8 API kapsamı (100), §9 kod düğümleri
> (110-119). Bu ailede §3-§6 GERÇEK OpenAI çağrısı yapar.

---

## MT-WF-001 — `PUT /api/workflows/{name}` yeni bir Sequential tanım oluşturur (`200`)

**Gerçek sonuç**
`HTTP: 200`, gövde `version:1, tenantId:"default", agentNames:["summarizer","translator"]`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-002 — Aynı adı tekrar `PUT` etmek günceller, `version` artar

**Gerçek sonuç**
`HTTP: 200`, `version:2`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-003 — `GET /api/workflows/{name}` veritabanında saklı bir tanımı döner

**Gerçek sonuç**
Gövde MT-WF-002'nin sonucuyla birebir aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-004 — Aynı uç, KODda tanımlı bir workflow için ayırt edici bir `404` döner (düzeltildi)

**Gerçek sonuç**
🚨 K-228 dil deseni (kusur değil): `title: "No editable definition"`
(Türkçe "Duzenlenebilir tanim yok" değil), `detail` workflow'un kodda
tanımlı olduğunu ve düzenlenebilir bir tanım taşımadığını açıklıyor —
generic mesaj değil, ayırt edici. Anlamca tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-005 — `GET /api/workflows` kod + veritabanı birleşik liste; isim çakışmasında KOD kazanır

**Gerçek sonuç**
`PUT` → `200` (kabul edildi). Liste: `summarize-and-translate` →
`origin:"Code", kind:null, agentNames:[]` — DB kaydı listede görünmez
oldu, tam beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-006 — `DELETE` veritabanı kaydını siler, sonraki `GET` `404` verir

**Gerçek sonuç**
`DELETE` → `204`, sonraki `GET` → `404` (K-228: "Workflow not found").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-007 — Var olmayan bir adı silmek → `404`

**Gerçek sonuç**
`404`, `title: "Workflow not found"` (K-228, anlamca "Workflow bulunamadi").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-008 — Kod-tanımlı bir adı silmeye çalışmak → `404`, çalışmaya devam eder

**Gerçek sonuç**
🚨 Adım 1: bu koşumda `204` döndü (`400` değil `404` bile değil) — spec'in
kendi kabul ettiği iki olası öncülden biri gerçekleşti: MT-WF-005'in
bıraktığı gerçek bir DB kaydı vardı (006'da silinmemişti, farklı bir ad
silinmişti), bu yüzden bu DELETE onu buldu ve sildi. Kusur değil, case'in
kendi notu bu iki yolu da öngörüyordu. Adım 2 atlandı (adım 1 farklı
sonuçlandığı için "True" kontrolü anlamsızlaştı). Adım 3 (asıl iddia):
workflow normal çalıştı, `WorkflowOutput` üretti — kod-tanımlı workflow
HTTP üzerinden **kaldırılamaz** iddiası doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-009 — Boşluktan ibaret ad → `400` "name alanı zorunludur"

**Gerçek sonuç**
`400`, `detail: "The workflow definition's 'name' field is required."` (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-010 — 🚨 `kind` alanı gövdede atlanırsa sessizce `Sequential`'a düşer

**Gerçek sonuç**
`200`, `kind:"Sequential"` — şüphe doğrulandı, hatasız ama sessiz varsayılan.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-011 — `agentNames` boş → `400`

**Gerçek sonuç**
`400`, `detail: "Workflow 'bos-katilimci' has no agents. 'agentNames' must carry at least one name."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-012 — Aynı agent adı iki kez → `400`

**Gerçek sonuç**
`400`, `detail`: "Agent 'summarizer' appears more than once in workflow 'tekrar-eden'. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-013 — `Concurrent` + tek agent → `400` (en az iki ister)

**Gerçek sonuç**
`400`, `detail`: "... 'Concurrent' pattern, which requires at least two agents; the list has 1."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-014 — `Magentic` + boş `managerAgentName` → `400`

**Gerçek sonuç**
`400`, `detail`: "... 'Magentic' pattern, and 'managerAgentName' is required. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-015 — `Magentic` + yönetici aynı zamanda katılımcı → `400`

**Gerçek sonuç**
`400`, `detail`: "... 'summarizer' appears as both manager and participant. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-016 — `GroupChat` + `managerAgentName` verilirse → `400`

**Gerçek sonuç**
`400`, `detail`: "... does not use 'managerAgentName' in the 'GroupChat' pattern. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-017 — `Sequential` + `handoffInstructions` verilirse → `400`

**Gerçek sonuç**
`400`, `detail`: "... does not use 'handoffInstructions' in the 'Sequential' pattern. ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-018 — `Magentic` olmayan desende `requirePlanApproval: true` → `400`

**Gerçek sonuç**
`400`, `detail`: "... Plan approval belongs only to the 'Magentic' pattern; ..."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-019 — `maxIterations: 0` → `400`

**Gerçek sonuç**
`400`, `detail`: "Workflow 'sifir-tur''s 'maxIterations' value must be positive."

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-WF-020 — Var olmayan agent adı KAYITta kabul edilir, RUN'da domain event verir

**Gerçek sonuç**
Spec'in kendi kod-okuma düzeltmesi doğrulandı: `PUT` → `200`. `run` akışı
`event: run` → `event: event (RunStarted)` → `event: event (RunFailed,
"Workflow 'hayali-agent' uses agent 'yok-boyle-bir-agent', but no such
agent exists in the catalog...")` → `event: done` — HTTP/bağlantı
düzeyinde `event: error` YOK, akış normal bitti. Birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
