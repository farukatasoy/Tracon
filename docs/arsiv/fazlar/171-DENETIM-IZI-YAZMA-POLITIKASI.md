# Faz 171 — Denetim İzi Yazma Politikası

> **Durum:** ✅ Tamamlandı (2026-09-15)
> **Plan onayı:** onaylandı — kullanıcı "sıradaki fazı geliştir" dedi
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-234** · [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) **BL-047**
> **Önkoşul:** Yok. K-776 sözleşmeyi zaten sabitledi
> **Paketler:** `Tracon.Core`, `Tracon.AspNetCore`, `Tracon.Abstractions`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — iki giriş. `PublicAPI.Shipped.txt` bugün **0 giriş** taşır (ölçüldü 2026-09-15), yani ekleme Faz 7'den önce ucuzdur; sonra bir sürüm kararı olur
> **Tüketici yüzeyi:** site: `concepts/governance.md` (garanti tablosu satırı), `guides/observability.md` (yeni sayaç)
> · sevk edilen: `IAuditLog` XML sözleşme metni, `capabilities.md` audit satırı
> **Manuel test alanı:** `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 41c4590e:docs/arsiv/fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md
> ```
>
> Damıtıldı 2026-09-15 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

K-776 denetim izinin garanti ayrımını **yayımlanmış bir sözleşme** hâline getirdi: altı işlem fail-closed'dır, kalan her audit yazımı best-effort'tur. Sözleşme yayımlandı, ama kodda onu zorlayan tek bir yer yok.

## Bitiş Ölçütleri (DoD)

- [x] `grep -rn "WriteAuditOrThrowAsync" src/` **hiç eşleşme döndürmez** — doğrulandı
- [x] `IAuditLog.WriteAsync` yalnız `AuditRecorder.cs` içinde çağrılıyor (satır 65 ve 133); `AuditWritePolicyTests` bunu 500+ dosya tarayarak zorluyor
- [x] Altı fail-closed işlemin altısı da `AuditRecorder.WriteOrThrowAsync` çağırıyor — **dokuz** çağrı yeri (grant/revoke, trigger upsert/delete ve onayın iki yüzeyi ikişer). Onayın in-band yüzeyi bağımsız denetimde bulundu ve bu fazda kapatıldı (🔴 1)
- [x] `audit_log` yazılamazken approval kararı uygulanmıyor — `FailClosedAuditTests` kanıtlıyor; testin yükü taşıdığı, yolu geçici olarak best-effort'a düşürüp **iki testin kırmızıya döndüğü** görülerek doğrulandı
- [x] `audit_log` yazılamazken yönetim çağrısı tamamlanıyor ve `tracon.audit.write_failures` artıyor — gerçek süreçte `swallowed` 2, `refused` 1 ölçüldü
- [x] `IAuditLog` XML sözleşmesi altı istisnayı adıyla yazıyor — `SeamContractDocumentationTests`'in borç defteri bir kalem **küçüldü** (`IAuditLog:delivery`)
- [x] `AuditWritePolicyTests` rakamla iddia ediyor: 6 işlem · 9 çağrı yeri · 500+ taranan dosya. Boş küme taraması testi **düşürür**
- [x] Dört doğrulama kapısı — `kapi.py kapanis` (aşağıda)
- [x] `samples/Tracon.Api` ile gerçek koşum yapıldı, çıktı *Süreç Ölçümü*'ne yazıldı
- [x] `secret` taraması boş döndü (`kapi.py tarama`: ✅ temiz)
- [x] Manuel kabul case'leri eklendi (MT-SEC-190…193); dördü de otomatikleştirildi ve koşuldu
- [x] `faz-denetim` koşuldu (taze bağlamlı bağımsız denetçi); **1 🔴 · 5 🟡 · 3 🟢** bulundu ve **hepsi kapandı** — *Denetim Bulguları*
- [x] `docs-site/` güncellendi; `npm run check` temiz (1145 sayfa, 0 kırık bağlantı, 0 SEO hatası)
- [x] `YAYIN-HAZIRLIK.md`'de **BL-047 kapandı** olarak işaretlendi; § *Bu turun bulguları* 8. satır da ✅ oldu

### Doğrulama komutları — gerçek çıktı

```bash
$ grep -rn "WriteAuditOrThrowAsync" src/
# (eşleşme yok)

$ grep -rn "\.WriteAsync(" src/ --include="*.cs" | grep -iE "auditlog\.WriteAsync"
src/Tracon.Core/Audit/AuditRecorder.cs:65:            await auditLog.WriteAsync(
src/Tracon.Core/Audit/AuditRecorder.cs:133:            await auditLog.WriteAsync(

$ grep -rn "AuditRecorder.WriteOrThrowAsync(" src/ --include="*.cs" | wc -l
9

# Bir action adı kaç yerden yazılıyor? (denetim 🔴 1'in bulunma yolu)
$ grep -rn '"approval.decision"' src/ --include="*.cs"
src/Tracon.AspNetCore/Endpoints/ApprovalEndpoints.cs:233:            action: "approval.decision",
src/Tracon.AspNetCore/Internal/ToolApprovalResolver.cs:106:            action: "approval.decision",
```

Sayaç `samples/Tracon.Api`'nin gerçek sürecinden okundu — örnek bir Prometheus
ucu yayımlamaz (`/metrics` → 404), OpenTelemetry tüketicinin seçimidir:

```
$ dotnet-counters collect --process-id <pid> --counters Tracon --format csv
tracon.audit.write_failures[tracon.audit.action=agent.create;tracon.audit.outcome=swallowed;tracon.tenant.id=default]  2
tracon.audit.write_failures[tracon.audit.action=trigger.delete;tracon.audit.outcome=refused;tracon.tenant.id=default]  1
```

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez** — sonraki oturumun en değerli bilgisidir.

| Sapma | Karar | Gerekçe |
|---|---|---|
| 🚨 **Plan sayacın `AuditRecorder`'a NASIL ulaşacağını söylemiyordu.** `AuditRecorder` `static`'tir ve DI görmez | `TraconMetrics? metrics` **her iki metoda da zorunlu-nullable parametre** olarak eklendi; 46 best-effort çağrı yerinin hepsi derleyici tarafından ziyaret edildi | Üç seçenek ölçüldü. (a) `IAuditLog` dekoratörü: tek kayıt gibi görünüyor ama **dört** yerde yeniden uygulanmalı (`Core` + üç sağlayıcı `Replace` eder) **ve** tüketicinin kendi `IAuditLog` kaydı onu sessizce düşürürdü — sayaç Tracon'un kendi yazma yolunu ölçmeli, belirli bir kaydı değil. (b) Opsiyonel parametre: yeni çağrı yeri sessizce atlar — bu repo'nun tekrar eden kusur sınıfı. (c) Zorunlu-nullable: derleyici her çağrı yerini zorlar. **(c) seçildi.** Bedeli 9 dekoratör ctor'u + 36 kayıt satırı; hepsi mekanik ve tip uyuşmazlığı derlemede yakalanır |
| Plan `refusalVerb` (fiil) diyordu | Tek bir `refusal` parametresi — **cümlenin ilk yarısının tamamı** | Dört kopyanın cümleleri yalnız fiilde değil **öznede** de farklıydı (`"Script permission 'x'"` · `"Script 'x'"` · `"The approval decision with id 'x'"` · `"'x'"`). Fiil-only bir imza özneyi tek tipe indirirdi; iki parametre yerine bir parametre dördünü de **birebir** korur |
| Plan *"`refusalVerb` boşsa genel cümle kullanılır"* diyordu | Boş `refusal` **`ArgumentException` atıyor** | Genel bir cümle ("the operation was not applied") çağırana hangi işlemin reddedildiğini söylemez — o cümlenin tek işi budur. Dokuz çağrı yerinin hiçbirinde boş olamaz; boşsa bu bir programlama hatasıdır ve sessiz bir genelleme değil, gürültülü bir hata hak eder. `An_empty_refusal_is_rejected_before_anything_is_written` sabitliyor |
| Planda yoktu: `TimeProvider? timeProvider` parametresi | Eklendi | `SandboxedSkillScriptRunner` audit satırını `_timeProvider.GetUtcNow()` ile damgalıyordu, diğer beşi `DateTimeOffset.UtcNow` ile. "Düz taşıma" bu farkı sessizce silerdi ve runner'ın testleri sahte saatini kaybederdi |
| `CanaryEvaluationService` "düz taşıma" sanılıyordu | Taşındı **ama** çağrı bir `try`/`catch (TraconException)` içine alındı | Ölçüldü: eski kod `throw` **etmiyordu** — `LogWarning` yazıp `return` ediyordu. `WriteOrThrowAsync` artık atıyor; istisna bir kare yukarıda `TickAsync`'in `catch`'ine düşerdi ve mesaj "Canary evaluation failed" olurdu. Açık `catch` niyeti koda yazar: rollback uygulanmaz, tarama ölmez. Davranış aynı, **kaydı** daha iyi (artık `LogError` + sayaç da var) |
| Açık Soru §1 — sayaç fail-closed yolda da artsın mı? | **A (öneri)**: tek sayaç, `tracon.audit.outcome` etiketiyle `swallowed`/`refused` | Ölçüldü: canlı süreçte iki seri ayrı ayrı göründü (`agent.create`→`swallowed` rate 2, `trigger.delete`→`refused` rate 1). Tek sorgu "iz ne sıklıkla yazılamıyor", etiket "kaç işlem durdu" |
| Açık Soru §2 — `DataSubjectEndpoints` taşınabilir mi? | **A: taşındı** | `IDataSubjectStore.EraseAsync`'in `beforeCommitAsync` geri çağrısı `Func<…, ValueTask>`'tir ve atınca her `DELETE` geri alınır — `WriteOrThrowAsync`'in şekliyle **birebir** oturur. Çağrı geri çağrının **içinden** yapıldı, dışından değil |
| Açık Soru §3 — kaynak taraması mı, analyzer mı? | **A: kaynak taraması** | Emsal `AuditCoverageTests` de kaptan doğrular. Boş-küme tuzağına karşı tarama **500'den fazla dosya okuduğunu** ayrıca iddia eder |
| 🚨 **Planın kanıt tablosu yanlıştı**: *"`SandboxedSkillScriptRunner` `LogError` yazmaz"* | Düzeltildi | Ölçüldü (`git show 33abea50:<dosya> \| grep -c LogError`): dört kopyanın **dördü de** `LogError` yazıyordu. Ayrışan yer `CanaryEvaluationService`'ti — o bir kopya değil, doğrudan çağrıydı ve `LogWarning` yazıp `return` ediyordu. Yanlış iddia alan hafızasına ve bir test yorumuna kadar taşınmıştı; üçü de düzeltildi |
| Planda yoktu: `AuditContentPolicyTests`'in deseni genişletildi | `\bAuditRecorder\.Write(?:OrThrow)?Async` | Desen yalnız `WriteAsync` arıyordu; beş dosya fail-closed yola geçince **izleme listesinden düştüler** ve testin ikinci yarısı (`Every_allowed_file_still_exists`) kırmızı döndü. Düzeltme listeden silmek değil, deseni genişletmektir: fail-closed yol da aynı `AuditEntry`'yi yazar, yani içerik politikası ona da uygulanır |
| Planda yoktu: iki sevk edilen metin düzeltildi | `trigger.delete`'in reddi artık *"was not deleted"* (önce *"was not saved"*); `governance.md` satırı *"Saving or deleting an inbound trigger"* | Ortak kopya tek cümleyi hem upsert hem delete için kullanıyordu, site tablosu da yalnız kaydetmeyi sayıyordu. İkisi de **silmenin de fail-closed olduğunu** gizliyordu |
| Planda yoktu: `PublicAPI.Unshipped.txt` **tamamen yeniden sıralandı** (457+/451−) | Bırakıldı | Taban dosya sıralı değildi; girişleri eklerken tamamı sıralandı. Küme farkı doğrulandı — `diff <(sort taban) <(sort yeni)` yalnız 6 yeni + 2 değişen gösterir, **kayıp yok**. Bir kerelik gürültü; bundan sonraki `git diff`'ler daha okunur |
| DoD'nin `curl /metrics` komutu | Koşulamadı; yerine `dotnet-counters` ile canlı süreçten okundu | `samples/Tracon.Api` bir Prometheus ucu **yayımlamıyor** (`/metrics` → 404) — OpenTelemetry tüketicinin seçimidir (K-063 deseni). Ölçüm yine gerçek süreçten alındı |

### Site senkron kuralları — gerekçeli geçiş

`dokuman-bakim.py --site-denetle` iki kuralı tetikledi; ikisinin de hedef sayfası
**kasıtlı olarak** değişmedi:

| Kural | Neden tetiklendi | Neden hedef sayfa değişmedi |
|---|---|---|
| `http-api` → `docs/openapi/tracon.json` · `http-api.md` | 12 endpoint dosyası değişti | Değişiklik yalnız `[FromServices] TraconMetrics metrics` parametresidir. Yeni rota yok, istek/yanıt şekli yok, durum kodu yok — ölçüldü: `git diff` içinde tek bir `MapGet`/`MapPost`/`Accepts<>`/`Produces<>` satırı eklenmedi ve `docs/openapi/tracon.json` **hiç değişmedi** (`OpenApiSnapshotTests` yeşil). `[FromServices]` OpenAPI belgesine girmez |
| `kalicilik` → `getting-started/persistence.md` | Üç SQL sağlayıcısının builder uzantısı değişti | Değişiklik yalnız dekoratör kayıtlarına `provider.GetRequiredService<TraconMetrics>()` eklenmesidir. Migration yok, şema yok, yeni seçenek yok, bağlantı davranışı yok |

Fazın gerçek tüketici yüzeyi `guides/observability.md` (yeni sayaç ve iki etiket)
ve `concepts/governance.md`'dir (fail-closed tablo satırı düzeltildi, sayaca bağ
verildi); ikisi de güncellendi ve `guvenlik-kiraci` ile `cekirdek-kavram`
kuralları ✅ döndü.

## Bu Fazda Verilen Kararlar

Yeni bir `K-*` kaydı **açılmadı**. Bu faz K-776'yı uygulayan bir yeniden
düzenlemedir; hiçbir işlemin garanti sınıfı değişmedi ve public yüzey büyümesi
bir uyumluluk sözü taşımıyor (`PublicAPI.Shipped.txt` boş, K-603).

⚠️ `docs/KARARLAR.md` bütçesi **%0 boş** (419.359 / 420.000 B). Bir sonraki
`K-*` kaydından önce `karar-damit` koşmak zorunludur — YAYIN-HAZIRLIK bunu zaten
yazıyor ve bu faz bütçeyi harcamayarak o zorunluluğu **ertelemedi**.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan revize edilmedi, sapmalar yukarıda yazıldı |
| Düzeltme turu sayısı | 9 (analyzer: MA0023 · MA0006 · MA0002 · CS1573; ratchet sayısı 7→8→9; `[FromServices]` iki iç yardımcıda geçersiz; SSE çerçevesi JSON sanıldı; `using` sırası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **1 / 0 / 0** — gerçekti ve kapatıldı (in-band onay yolu). Ayrıca 5 🟡 (hepsi kapandı, biri kalıcı hafızadaki **yanlış bir olguydu**) ve 3 🟢 |
| Fazın ürettiği regresyon | 0 — iki mevcut test kırıldı, ikisi de **kasıtlı davranış değişikliğinin kanıtıydı** (`AuditContentPolicyTests` deseni; `DataSubjectEndpointTests` istisna tipi) |
| Faz kapandıktan sonra bulunan kusur | — |

**Kanıt: testler yükü taşıyor.** `ApprovalEndpoints` geçici olarak best-effort
yola düşürüldü; `FailClosedAuditTests`'in **iki** testi kırmızıya döndü
(`An_approval_decision_is_not_applied_…`, `The_same_approval_is_decided_once_…`).
Geri alındı, yeşile döndü. Sessiz bir düşüş artık sessiz değildir.

**Kanıt: gerçek süreç, gerçek veritabanı.** `samples/Tracon.Api` SQLite ile
koştu; `tracon_audit_log` üzerine `RAISE(ABORT)` yazan bir trigger kondu:

| Ne yapıldı | Gözlenen |
|---|---|
| `DELETE /api/triggers/slack` (fail-closed) | `HTTP 500`; `GET /api/triggers` → **1 trigger hâlâ duruyor**; log'da `LogError` + `TraconException: 'trigger:slack' was not deleted because it could not be written to the audit trail.` |
| `POST /api/agents` (best-effort) | `HTTP 201`; `GET /api/agents/faz171-agent` → **200, agent gerçekten var**; log'da yalnız `LogWarning` |
| `dotnet-counters --counters Tracon` | `tracon.audit.write_failures[action=agent.create;outcome=swallowed]` → **2** · `[action=trigger.delete;outcome=refused]` → **1** |
| Trigger düşürüldü, `DELETE` tekrarlandı | `HTTP 204`; trigger silindi; `trigger.delete` audit satırı yazıldı |
| `GET /api/audit/verify` | `{"status":"Valid","entriesChecked":2}` — reddedilen deneme zincirde **boşluk bırakmaz**, çünkü hiç satır yazılmamıştır |

## Denetim Bulguları

> Bağımsız denetçi (`faz-denetim`, taze bağlam) `33abea50` → çalışma ağacını yargıladı.
> **1 🔴 · 5 🟡 · 3 🟢.** Hepsi kapandı.

### 🔴 1 — `approval.decision`'ın ikinci yolu yayımlanmış garantiyi yalanlıyordu

**Bulgu.** Bir onay kararı **iki** yüzeyden gelebilir: onay kutusu
(`POST /api/approvals/{id}/decide`) ve **run gövdesinin kendisi**
(`AgentRunRequest.Approvals` → `ToolApprovalResolver`). Birincisi fail-closed'dı;
ikincisi `AuditRecorder.WriteAsync` çağırıyordu — **best-effort**. Oysa
`governance.md:185` *"An approval decision → The decision is not applied"* diyor ve
`:299` bunu *"not applied **at all**"* diye pekiştiriyor.

🚨 **Bu fazın kendisi çelişkiyi görünür kılıyordu.** Yeni sayaç, `audit_log`
bozulduğunda tüketicinin dashboard'una
`tracon.audit.write_failures{action=approval.decision,outcome=swallowed}` basacaktı —
yani yayımlanmış garantinin yanlış olduğunu **bizim ölçümümüz** ilan edecekti.

**Karar: (a) — in-band yol fail-closed'a geçirildi.** Denetçi üç seçenek sunmuştu:
(a) yolu düzelt, (b) garanti metnini *"out-of-band approval decision"* diye daralt,
(c) ertele. (b) yayımlanmış bir güvenlik garantisini **daraltmak** olurdu ve bunun
gerekçesi "kodumuz öyle yapmıyor"dan ibaret kalırdı — K-089'un gerekçesi (onay kararı
geri alınamaz bir eylemdir) hangi yüzeyden geldiğine bakmaz. (c) bir sonraki faza
**bilinen bir yalan** devretmek olurdu.

Fazın kapsam sözü *"hiçbir işlemin garanti sınıfı değişmez"*di; bu sözü **çiğnemiyor**,
uyguluyor: garanti sınıfını K-776 zaten belirlemişti, kod ona uymuyordu.

| Ne | Nerede |
|---|---|
| Düzeltme | `ToolApprovalResolver.cs:99-118` — `WriteOrThrowAsync`, ve yazım `contents.Add` **öncesine** alındı |
| Davranış testi | `FailClosedAuditTests.An_in_band_approval_decision_is_refused_the_same_way_as_an_out_of_band_one` |
| Ratchet | `AuditWritePolicyTests`: 8 → **9** çağrı yeri; `FailClosedOperations` onay kararını iki yüzeyle listeliyor, "altı işlem" iddiası korunuyor |

Ret çağırana akışın **`error` çerçevesi** olarak ulaşır ve onaylanan tool **hiç
çalışmaz** — test ikisini de iddia eder:

```
event: error
data: {"type":"TraconException","message":"The approval decision for tool 'cancel_order'
       was not applied because it could not be written to the audit trail."}
```

### 🟡 Kapananlar

| # | Bulgu | Kapanış |
|---|---|---|
| 2 | `DataSubjectEndpoints.cs:1` `using` sırası bozuk — repo'daki **tek** ihlal (1/~1500). DoD *"dört kapı sıfır uyarı"* diyordu ama `dotnet format` o dosyadan sonra koşulmamıştı | `dotnet format` ile düzeltildi; `--verify-no-changes` **temiz** |
| 3 | `CanaryEvaluationService.cs:180` `var now` taşımadan kalan ölü satır (IDE0059 yalnız `suggestion`, kapılardan geçerdi) | Silindi |
| 4 | `script.grant` / `script.revoke` için **hiçbir davranış testi yoktu** — ne yeni ne eski. Riskler tablosu ise *"`FailClosedAuditTests` her altı işlemi ayrı ayrı koşar"* diye **yanlış** iddia ediyordu | `AuditingSkillScriptGrantStoreTests` (3 test) eklendi. 🚨 Yükü taşıdığı ölçüldü: `GrantAsync`'in iki satırı yer değiştirildi → **üç mimari test de yeşil kaldı**, yalnız yeni davranış testi düştü. Riskler tablosundaki iddia gerçekle değiştirildi |
| 5 | 🚨 **Kalıcı hafızaya yanlış bir olgu yazılmıştı**: *"dört kopyadan biri `LogError` yazmıyordu"*. Kaynağı planın kanıt tablosuydu; kopyalandı | `git show 33abea50:<dosya> \| grep -c LogError` → **dördü de 1**. İddia yanlış. Üç yerde düzeltildi: alan hafızası, faz dokümanı (iki yer), `AuditRecorderTests` yorumu. Ayrışan yer `CanaryEvaluationService`'ti (kopya değil, doğrudan çağrı) |
| 6 | Plandan sapma yazılmamış: boş `refusal` planda *"genel cümle"*, kodda `ArgumentException` | *Plandan Sapmalar*'a satır eklendi |

### 🟢 Aday / kozmetik

| # | Bulgu | Karar |
|---|---|---|
| 7 | `AuditWritePolicyTests`'in deseni **değişken adına** bağlı: `IAuditLog log = …; log.WriteAsync(…)` şeklini görmez | Açık Soru §3'te "ucuz ama **kırılgan**" diye bilerek seçildi ve gerekçesi yazıldı. Analyzer'a geçmek ayrı bir fazdır — `ADAYLAR.md`'ye düşer |
| 8 | `PublicAPI.Unshipped.txt` tamamen yeniden sıralandı (457+/451−) | *Plandan Sapmalar*'a yazıldı; küme farkı `diff <(sort) <(sort)` ile doğrulandı, kayıp yok |
| 9 | Public API sayısı dokümanda yanlış (1463 → gerçek 1462) | Düzeltildi |

### Denetçinin temiz bulduğu başlıklar

Test tiyatrosu **yok** (her fonksiyonel test mutasyonun olmadığını iddia ediyor, yalnız
istisna tipini değil) · test seviyeleri doğru (sınır geçen davranış fonksiyonel) · hata
yolları tam (iptal her iki yolda yutulmuyor ve sayacı artırmıyor) · **imza-gövde kayması
yok** (46 + 9 çağrı yerinin hepsi `metrics` geçiyor; 27/27 dekoratör kaydı dört dosyada
da tam) · plan dışı public API gerekçeli · sevk edilen XML'de faz/karar numarası yok ·
muafiyet listeleri ve taban çizgileri **yalnız küçüldü** (`seam-contract-baseline` 172 →
171; `AllowedCallSites` budanmadı, desen genişletildi).

## Sonraki Faza Devir Notu

- 🚨 **`IAuditLog.WriteAsync`'i `src/` içinde doğrudan çağırma.** Tek izinli yer
  `AuditRecorder.cs`'tir ve `AuditWritePolicyTests` bunu 500+ dosya tarayarak
  zorlar. Best-effort için `WriteAsync`, fail-closed için `WriteOrThrowAsync`.
- 🚨 **Bir `action` adı BİRDEN ÇOK yüzeyden yazılabilir; garantiyi yüzey değil
  İŞLEM taşır.** `approval.decision` iki yerden yazılır — onay kutusu
  (`ApprovalEndpoints`) ve run gövdesi (`ToolApprovalResolver`) — ve ikincisi faz
  171'e kadar **best-effort**'tu, yani yayımlanmış fail-closed garantisini sessizce
  yalanlıyordu. Bağımsız denetim buldu. Yeni bir garanti yazarken
  `grep -rn '"<action>"' src/` ile **kaç yerden yazıldığını** say; bir tanesini
  düzeltmek garantiyi sağlamaz.
- 🚨 **Fail-closed kümesi altı işlemdir ve bu YAYIMLANMIŞ bir güvenlik
  garantisidir** (K-776). Kümeye eklemek veya çıkarmak **dört** yeri birlikte
  değiştirir: `AuditWritePolicyTests.FailClosedOperations`, `concepts/governance.md`
  tablosu, `reference/security-policy.md` kapsamı ve `capabilities.md` audit satırı.
  Test "altı işlem · sekiz çağrı yeri" sayılarını **rakamla** iddia eder; ikisi
  farklı sorulardır (grant/revoke ve trigger upsert/delete ikişer çağrı yeridir).
- **`AuditRecorder`'a yeni bir parametre eklemek 55 çağrı yerini ziyaret ettirir.**
  Bu kasıtlıdır (zorunlu-nullable `metrics` deseni), ama ucuz değildir: 9 dekoratör
  ctor'u + 36 kayıt satırı (`Core` + üç SQL sağlayıcısı) + 15 endpoint handler'ı.
  Yeni bir bağımlılık gerekiyorsa **önce** onu gerçekten her çağrı yerinde mi
  istediğini sor.
- **Bir `Auditing*` dekoratörüne ctor parametresi eklemek DÖRT dosyada kayıt
  değiştirir** — `Registration.Storage.cs` **ve** `Tracon.{PostgreSql,SqlServer,
  Sqlite}`'ın `services.Replace` listeleri. Üçünden birini atlamak derlenmez
  (pozisyonel argüman), ama bir **sağlayıcıyı** atlamak sessizce o sağlayıcıda
  eski davranışı bırakır.
- **Sayaç etiketi `action`'dır, `entity` DEĞİL.** `entity` tüketici verisidir;
  etikete koymak her agent/trigger/session'a ayrı bir zaman serisi verirdi. Yeni
  bir audit sinyali eklerken aynı ayrımı koru.
- `AuditContentPolicyTests` ile `AuditWritePolicyTests` **aynı çağrı yerlerine
  iki ayrı soru** sorar: ne yazılabilir · yazılamazsa ne olur. Yeni bir audit
  yazan dosya ikisini de kırar; ikisini de yanıtla.
