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
