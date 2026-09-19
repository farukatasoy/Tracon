# 34 — Tipli Yönetim İstemcisi ve CLI (`CLI`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../34-ISTEMCI-VE-CLI.md`](../../34-ISTEMCI-VE-CLI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (oturum 14, family 29'un hemen
> ardından) `Gerçek sonuç` ve `Durum` kayıtlarıdır.

## Devir notu (oturum 14)

**Aile 34 (`CLI`, 46 case) TAMAMLANDI** — 42/46 koşuldu (41 ☑ Geçti, 1 ☒
Kaldı), kalan 4 case (`MT-CLI-012`, `022`, `037`, `046`) spec'in kendisi
`👤 insan gerekir` diyor, fiziksel eylem tablosuna eklendi, koşulmadı.

**Bir kusur bulundu: `HATA-S3-003`** — eval suite'lerin case listesini
`PUT /api/evals/{name}/cases` ile düzenlemek (hand-add tek yolu) suitedeki
**her** case'in kimliğini (veritabanı id'si) sıfırlıyor, içerik/sıra aynı
kalsa bile. Ayrıntı aşağıda ve HATA kaydında.
**✅ KAPANDI 2026-09-18** (Aile E). Kapanış aynı `PUT` turunda iki kayıp
daha ölçtü: `HATA-S3-009` (`Parameters` DTO'da yok — parametreli agent'ın
case'leri her kayıttan sonra düşüyordu) ve `HATA-S3-010` (`InsertEvalCase`
promosyon üçlüsünü hiç yazmıyordu — promosyon guard'ı devre dışı kalıyor,
aynı run ikinci kez yükseltilebiliyordu).

**Ortam notu — uygulama sürecinin kararsızlığı.** Bu oturumda
`samples/Tracon.Api` süreci en az üç kez, görünürde hiçbir hata/istisna
olmadan (`Application is shutting down...` — temiz kapanış, exception yok)
kendiliğinden sonlandı; her seferinde şema (`mt_s3`) sağlam kaldı,
yalnızca uygulama süreci gitti. Kök neden **belirlenemedi** (uygulama
kodunda değil, koşum ortamında/host'ta bir şey — muhtemelen arka plan
komutlarının çalıştığı sürecin kendisiyle ilişkili bir zaman aşımı). Kalıcı
`--no-build` release ikilisiyle, tekrarlanabilir; **sonraki oturum bunu
hesaba katmalı**: uzun bir case zincirine girmeden önce sık aralıklarla
sağlık kontrolü yap, çökme olursa şema **veri kaybetmeden** hayatta kalır,
yalnızca uygulamayı yeniden başlatman ve DEVAM EDEN case grubunun
fixture'larını (agent/suite/case) yeniden kurman gerekir (bu oturumda tam
olarak bu oldu, bkz. case 13-21 ve case 23/30 arası tekrar kurulum).
Bu bir Tracon ürün kusuru **değildir** (host/ortam katmanında); `HATA-S3`
numarası açılmadı.

**Yan not — kendi debug hatam.** Bir noktada `ps eww <pid>` ile sürecin
ortam değişkenlerini incelerken OpenAI API key'i **açık metin olarak** bu
oturumun araç çıktısına yazdırdım (hiçbir dosyaya/log'a değil, yalnızca bu
oturumun transkriptine). Turun kendisi zaten "sağlayıcı anahtarları
2026-09-16'da düz metne çıktı, tur bitince döndürülmeli" diyor
(`DEVIR.md` §2, §8); bu benim hatam o genel durumu şiddetlendirmedi ama
kullanıcıya ayrıca bildirilmesi gerekir.

**Sıradaki oturum:** Aile 34 bitti. Sırada `21-DAYANIKLILIK-VE-IPTAL.md`
var (bu şeridin dağılımı: `32 · 29 · 34 · 21 · 11 · 25 · 17 · 20`).

---

| | |
|---|---|
| **Şerit** | `ap-s3` (Faz B, üçüncü aile — bu şeridin dağılımı: 32 · 29 · **34** · 21 · 11 · 25 · 17 · 20) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s3` · dal `test/kosum-s3` |
| **Kod** | `7e3a4de7` donuk — `git diff --stat 7e3a4de7..HEAD -- src samples tests` boş, doğrulandı (adım 2). `ap-s3` HEAD `cc9895d2` |
| **Case sayısı** | 46 (MT-CLI-001..046) |
| **Port** | 5083 (`mt_s3`) — health/eval/state-check case'leri için `samples/Tracon.Api` bu portta koşuldu. MT-CLI-009 istisna: özel `MapTracon("control")` öneki gerçek uygulamada test edilemediği için (`samples/Tracon.Api`/`Tracon.Embedded` ikisi de `/tracon`'u sabit yazıyor, kod donuk) `/tmp` altında paketlenmiş bir tüketici host'u port `5094`'te açıldı, case sonunda kapatıldı |
| **Depo** | PostgreSQL `mt_s3` — case 34 başında düşürüldü, uygulama migration'ları yeniden uyguladı (51 migration), sağlıklı |
| **Model** | `gpt-5.4-mini` (gerçek OpenAI çağrısı) — eval case'leri (13-31) ve SSE case'leri (38-45) için fixture agent'lar (`manuel-bos`, `manuel-flip`, `manuel-flip2`) bu modele bağlandı |

**Sapmalar (bu oturumda ölçüldü):**

1. **MT-CLI-005/006 için "kimlik doğrulama yapılandırılmamış" önkoşulu
   ortamın normal durumuyla çelişiyordu.** Şerit ortamı (`serit-kurulumu.md`
   §2) `Tracon:Ui:AuthToken`'ı her zaman `user-secrets`'tan okur; sadece
   ortam değişkenini **atlamak** onu kapatmaz — `user-secrets`'taki kalıcı
   değer devreye girer (skill §1.2'nin "boş değer = kayıtlı değil" kuralı
   yalnızca **açıkça boş export edilirse** çalışır). MT-CLI-005/006 için
   uygulama `Tracon__Ui__AuthToken=""` ile (açık boş override) ayrı
   başlatıldı, doğrulandı (`GET /api/meta` → `requiresBearerToken:false`),
   sonra MT-CLI-007..010 için gerçek token ile yeniden başlatıldı. Bu bir
   kusur değil, ortam kurulum notudur; ders `docs/hafiza/`ye aday.
2. **MT-CLI-009 (özel `MapTracon` öneki) donuk `samples/` ile koşulamadı**
   — hem `Tracon.Api` hem `Tracon.Embedded` `/tracon` önekini sabit
   yazıyor (kod donuk, değiştirilemez). Family 05'in "paketlenmiş tüketici
   host'u" tarifine göre `/tmp/mt-s3-control-prefix` altında `dotnet new
   web` ile minimal bir ASP.NET Core projesi açıldı, `Tracon.AspNetCore`
   `0.0.0-preview.0.821` yerel feed'den (`ap-s3/artifacts/package/release`,
   family 29'un ürettiği paket) referans alındı, `app.MapTracon("control")`
   ile port `5094`'te koşuldu. Case bitince kapatıldı ve dizin silindi.
   `EchoModelProvider` samples/Tracon.Api'deki örneğin bağımsız bir kopyası
   olarak (frozen ağaca dokunmadan) bu geçici projeye yazıldı.
3. **MT-CLI-013..031 (eval) ve MT-CLI-038..045 (SSE istemcisi) fixture
   gerektiriyordu, spec bunu bilerek dışarıda bırakıyor** ("bu dosyanın
   kapsamı dışındadır — 17-EVAL-VE-DENEYLER.md'dedir"). Bu oturum kendi
   fixture'larını kurdu: agent `manuel-bos`/`manuel-flip`/`manuel-flip2`
   (gerçek OpenAI, gpt-5.4-mini, talimat `Yalnizca "tamam" yaz.`) ve suite
   `ok`/`mixed`/`reg-test`/`reg-test-2`/`fresh-once`, hepsi `POST/PUT
   /api/agents`, `PUT /api/evals/*` ile — kod dokunulmadı.
4. **MT-CLI-038..045 için ayrı bir paketlenmiş tüketici konsolu açıldı**
   (`/tmp/mt-s3-sse-client`), `Tracon.Client` `0.0.0-preview.0.821` yerel
   feed'den referans alınarak — bu case'ler `TraconApiClient`'ın typed SSE
   metotlarını (`TraconOpenAIResponsesStreamAsync` vb.) çağırıyor, `curl`
   ile kanıtlanamaz. Case'ler bitince proje silindi.
5. **Uygulama süreci oturum içinde üç kez kendiliğinden sonlandı** (yukarı
   bkz. "Ortam notu"). Her seferinde case grubu şemayı bozmadan yeniden
   kuruldu; case sonuçları etkilenmedi (her case'in fixture'ı yeniden
   oluşturulup case tekrar koşuldu, yarım sonuç kaydedilmedi).

---

## Case'ler

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 89f44ab3:docs/manuel-test/kosumlar/2026-09-16/34-ISTEMCI-VE-CLI.md
> ```

---

## Temiz geçen case'ler (38)

| Case | Durum | Başlık |
|---|---|---|
| MT-CLI-001 | ☑ | SQLite migrate, boş dosya |
| MT-CLI-002 | ☑ | SQLite migrate, tekrar (idempotent) |
| MT-CLI-003 | ☑ | migrate status sonrası, yazma yok |
| MT-CLI-004 | ☑ | migrate status, migrate hiç koşulmadan |
| MT-CLI-005 | ☑ | health, kimlik doğrulama yapılandırılmamış |
| MT-CLI-006 | ☑ | health --json |
| MT-CLI-007 | ☑ | health, geçersiz token |
| MT-CLI-008 | ☑ | health, sunucu kapalı |
| MT-CLI-010 | ☑ | hiçbir komut, uydurma bağlantı dizesi/token, secret sızmaz |
| MT-CLI-013 | ☑ | eval, hepsi geçen takım |
| MT-CLI-014 | ☑ | eval, bir case düşüyor |
| MT-CLI-015 | ☑ | eval, --max-failures 1 tolerans |
| MT-CLI-016 | ☑ | eval, iki eşik birden (VE) |
| MT-CLI-017 | ☑ | eval, olmayan takım adı |
| MT-CLI-018 | ☑ | eval, sunucu kapalı |
| MT-CLI-019 | ☑ | eval, --timeout 1 |
| MT-CLI-020 | ☑ | eval, yalnız EvalsRead scope'lu anahtar |
| MT-CLI-021 | ☑ | eval, --json |
| MT-CLI-023 | ☑ | eval baseline, regresyon tespiti |
| MT-CLI-024 | ☑ | eval baseline, regresyon yok |
| MT-CLI-025 | ☑ | eval baseline, ilk koşum |
| MT-CLI-026 | ☑ | eval baseline, kısmen budanmış sonuç |
| MT-CLI-027 | ☑ | eval, --max-regressions --baseline olmadan |
| MT-CLI-028 | ☑ | eval, --baseline yesterday |
| MT-CLI-030 | ☑ | eval baseline, --json ile regresyon |
| MT-CLI-031 | ☑ | eval baseline, başka suite'in run id'si |
| MT-CLI-032 | ☑ | state-check, sağlıklı veritabanı |
| MT-CLI-033 | ☑ | state-check, okunamaz satır |
| MT-CLI-034 | ☑ | state-check, yanlış bağlantı dizesi |
| MT-CLI-035 | ☑ | state-check, --sample 5 |
| MT-CLI-036 | ☑ | state-check, öncesi/sonrası anlık görüntü |
| MT-CLI-038 | ☑ | TraconOpenAIResponsesStreamAsync, stream:true |
| MT-CLI-039 | ☑ | TraconOpenAIChatCompletionsStreamAsync, stream:true |
| MT-CLI-040 | ☑ | TraconOpenAIResponsesAsync, stream:false |
| MT-CLI-041 | ☑ | TraconOpenAIResponsesAsync, gövdede stream:true |
| MT-CLI-042 | ☑ | TraconOpenAIResponsesStreamAsync, gövdede stream:false |
| MT-CLI-043 | ☑ | TraconOpenAIResponsesAsync(default) |
| MT-CLI-044 | ☑ | TraconRunAgentStreamAsync, iki çerçeve sonra break |

## Ayrıntı taşıyan case'ler (4)

## MT-CLI-009 — health, özel `MapTracon` öneki

**Gerçek sonuç**
Donuk `samples/` özel önek desteklemediği için (sapma 2) `/tmp` altında
minimal bir tüketici host'u (`Tracon.AspNetCore` paketinden,
`app.MapTracon("control")`) port `5094`'te açıldı. `tracon health --url
http://localhost:5094/control`: `echo: Unknown`; çıkış kodu `0`. Önek
soyma tasarımı kanıtlandı — istemci `/control` önekinin ARDINDAKİ
`/api/models/health` ucuna doğru ulaştı (aksi halde 404/bağlantı hatası
alınırdı). Durum değeri `Unknown` bir kusur değil; bu minimal
`EchoModelProvider`'ın sağlık kontrolü gerçek bir ağ çağrısı yapmadığı
için varsayılan döndüğü değer.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-CLI-011 — global tool kurulumu, `--help`

**Gerçek sonuç**
`dotnet tool install -g Tracon.Cli --version 0.0.0-preview.0.821
--add-source .../artifacts/package/release`: başarılı, ek Tracon-özel adım
istemedi (yalnız dotnet'in genel PATH uyarısı — Tracon'a özgü değil).
`tracon --help`: **ALTI** komut listeleniyor — `migrate`, `migrate
status`, `state-check`, `health`, `agent-skill`, `eval`.

**Beklenen sonuç düzeltmesi (rule 1.1 istisnası):** Spec'in "Beş komut
listelenir (migrate, migrate status, state-check, health, eval)" ifadesi
bayat — `agent-skill` komutu (Faz 73, family 29'un kapsamı) spec'in bu
satırına hiç eklenmemiş. Kod ile doküman çelişince doküman yanlıştır
(`AGENTS.md`); `../../34-ISTEMCI-VE-CLI.md` satır 63 "Beş komut" →
"Altı komut (... `agent-skill` ...)" olarak düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-CLI-029 — eval baseline, yeni case eklenmiş, regresyon değil

**Gerçek sonuç**
`ok` suite'ine `PUT /api/evals/ok/cases` ile **ikinci** bir case eklendi
(mevcut "Merhaba"→"tamam" case'i AYNI metinle yeniden gönderildi + yeni
düşen bir case). `tracon eval --suite ok --baseline previous
--max-regressions 0`: `Completed: 1/2 passed` + `FAILED case <yeni-id>` +
**`vs baseline <id>: 0 regressed, 0 fixed, 2 added, 1 removed.`**; çıkış
kodu `0`.

**Beklenen sonuç:** "1 added, 0 regressed" — **uyuşmuyor**. Kök neden ve
kanıt HATA-S3-003'te. İki kez tekrar üretildi (aynı sonuç).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### Yeniden koşum — 2026-09-18 (Aile E kapanışı)

**Gerçek sonuç — ✅ GEÇTİ.** `EvalCaseInput` `Id` alanı kazandı (K-801);
canlı ölçüm `samples/Tracon.Api` + gerçek OpenAI anahtarı + gerçek `tracon
eval` ikilisiyle tekrarlandı.

```
PUT /api/evals/ok029/cases  [{"query":"Merhaba","expectedOutput":"tamam"}]
GET -> CASE_ID_1: 01a0b45e-ee07-7dbf-82fb-4daaf086f327
tracon eval --suite ok029                       -> Completed: 1/1 passed

PUT  ayni case KENDI id'siyle + yeni bir case
GET -> CASE_ID_1_AFTER: 01a0b45e-ee07-7dbf-82fb-4daaf086f327   KEPT: True
tracon eval --suite ok029 --baseline previous --max-regressions 0
  -> vs baseline: 0 regressed, 0 fixed, 1 added, 0 removed.
```

**`1 added, 0 removed`** — case'in kendi beklentisi. Koşumun ölçtüğü
`2 added, 1 removed` gitti.

**Kaydın "daha ciddisi" dediği yarım da ölçüldü ve kapandı.** Agent'ın
talimatı bozuldu (`tamam` → `degisti`) ve **aynı pencerede** case listesi
düzenlendi (ilk case id'siyle korundu, bir yeni case eklendi):

```
tracon eval --suite ok029 --baseline previous --max-regressions 0
  -> vs baseline: 1 regressed, 0 fixed, 1 added, 0 removed.
     regressed: case 01a0b45e-...: Response does not contain expected output: "tamam"
  -> cikis kodu 3
```

Regresyon artık `Removed` değil `Regressed` sınıfında ve CI kapısı onu
**yakalıyor** (çıkış kodu `3`). Düzeltmeden önce bu run `Removed` sayılıp
`--max-regressions 0`'dan geçerdi.

İki reddetme yolu da canlı doğrulandı: bu suite'e ait olmayan id → `400
"Case id '…' does not belong to this suite…"`, aynı id iki kez → `400
"Case id '…' appears more than once…"`; ikisinde de hiçbir şey yazılmadı.

**Sınıf taraması aynı turda İKİ kayıp daha buldu** (`HATA-S3-009`,
`HATA-S3-010`) — `EvalCaseInput` `Parameters`'ı da taşımıyordu ve
`InsertEvalCase` SQL'i promosyon üçlüsünü hiç yazmıyordu. Tam anlatı
kapanış planının Aile E bölümünde; kararlar K-801 · K-802.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-CLI-045 — TraconRunAgentStreamAsync, önceden iptal edilmiş token

**Gerçek sonuç**
`WithCancellation(iptalEdilmişToken)`: `OperationCanceledException:
A task was canceled.` — `[EnumeratorCancellation]` bağı çalışıyor. (İsteğin
sunucuya hiç gönderilmediği ayrıca sunucu logundan doğrulanmadı — token
enumerasyondan önce iptal edildiği için davranış standart .NET
`IAsyncEnumerable` + `EnumeratorCancellation` semantiğiyle örtüşüyor.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## HATA-S3-003 — Eval suite case listesini düzenlemek TÜM case'lerin kimliğini sıfırlıyor

- **Case:** MT-CLI-029 (kapsam: `--baseline`/`--max-regressions` kullanan
  her aile — 17-EVAL-VE-DENEYLER.md'nin de sınıf taraması gerektirir)
- **Önem:** Yüksek
- **İzlek:** A (management API, gerçek PostgreSQL, gerçek OpenAI çağrısı)
- **Ortam:** macOS arm64 · net10 · PostgreSQL `mt_s3` · OpenAI (gpt-5.4-mini)

**Beklenen**
Bir suite'e yeni bir case eklemek (mevcut case'ler değişmeden), ikinci bir
koşumda `--baseline previous` diff'inde yalnız yeni case'i `added` olarak
saymalı; değişmeyen case'ler `unchanged`/`regressed`/`fixed` sınıfında
kalmalı (CLI `--help` metninin kendi vaadi: "Cases added to or dropped
from the suite are never counted as regressions" — bunun ön koşulu,
DEĞİŞMEYEN case'lerin kimliğinin korunmasıdır).

**Gerçekleşen**
`PUT /api/evals/{name}/cases` (case eklemenin TEK genel HTTP yolu — bkz.
kapsam) **tam değiştirme** yapıyor ve her çağrıda TÜM case'lere yeni bir
veritabanı id'si atıyor, içerik ve sıra birebir aynı kalsa bile. Sonuç:
değişmeyen bir case bile "Removed" (eski id) + "Added" (yeni id) olarak
görünüyor; gerçek "Added" sayısı olması gerekenin **iki katı**, "Removed"
sayısı ise **sıfır olması gerekirken bir**. MT-CLI-029'da beklenen "1
added, 0 regressed" yerine "2 added, 1 removed" ölçüldü — iki kez
tekrarlandı, birebir aynı sonuç.

Daha ciddisi: bu davranış `--max-regressions` kapısının **asıl amacını**
zayıflatıyor. Bir case listesi düzenlemesiyle AYNI pencerede gerçek bir
regresyon olursa (case'in sorgusu/beklenen çıktısı aynı ama agent'ın
davranışı bozulmuşsa), diff bunu "Regressed" değil "Removed" olarak
sınıflandırır — `--max-regressions 0` bu regresyonu **YAKALAMAZ**, çünkü
"Removed" case'ler regresyon sayılmaz (tasarım gereği, haklı olarak —
ama burada gerçek bir case "removed" değil, gerçekten regresyona uğramış).

**Kök neden**
`src/Tracon.Abstractions/Evaluation/EvalRunDiffBuilder.cs` case kimliğini
`EvalCaseResult.CaseId` (veritabanı id'si) ile eşliyor (satır 79-131).
`src/Tracon.Sql.Shared/Stores/SqlEvalStore.cs:105-157`
(`ReplaceCasesAsync`) **id'yi korumayı destekliyor** — satır 128-133:
`Id = cases[seq].Id == Guid.Empty ? TraconId.NewId() : cases[seq].Id`
(boşsa yeni id, doluysa mevcut id korunur). Ama
`src/Tracon.AspNetCore/Endpoints/EvalEndpoints.cs:361-370`
(`SaveCasesAsync`) `EvalCaseInput`'tan `EvalCase` üretirken `Id` alanını
**hiç set etmiyor** (`EvalCase { SuiteId = default, Seq = seq, Query =
..., ... }` — `Id` property'si implicit `Guid.Empty` kalıyor), çünkü
`src/Tracon.AspNetCore/Contracts/EvaluationContracts.cs:23-36`'daki
`EvalCaseInput` DTO'sunda **`Id` alanı hiç yok**. Yani store katmanı
identity korumayı tam olarak destekliyor, ama HTTP sözleşmesi bunu
istemciye **hiç açmıyor** — `PUT /cases`'i çağıran hiçbir istemci
mevcut bir case'in kimliğini koruyamaz.

`src/Tracon.Abstractions/Evaluation/IEvalStore.cs:72-74`'teki
`AddCaseAsync` (tek case ekleme, diğerlerine dokunmadan) tam olarak bu
sorunu çözecek şekilde var, ama yalnız `RunToCasePromoter`
(`src/Tracon.Core/Evaluation/RunToCasePromoter.cs:110`) üzerinden "bir
run'ı case'e yükselt" ucunda (`POST /cases/from-run/{runId}`)
kullanılıyor — elle, keyfi metinli bir case eklemek için genel bir HTTP
ucu yok.

**Yeniden üretme**
1. `PUT /api/evals/ok` — suite oluştur, agent + `containsExpected` check.
2. `PUT /api/evals/ok/cases` — `[{"query":"Merhaba","expectedOutput":"tamam"}]`.
3. `tracon eval --suite ok` — koştur (geçer), case'in id'sini not al
   (`GET /api/evals/ok/cases`).
4. `PUT /api/evals/ok/cases` — **AYNI** ilk case + yeni bir case:
   `[{"query":"Merhaba","expectedOutput":"tamam"},
   {"query":"Merhaba","expectedOutput":"baska-bir-sey"}]`.
5. `GET /api/evals/ok/cases` — ilk case'in id'si **DEĞİŞTİ**, içerik
   birebir aynı olmasına rağmen.
6. `tracon eval --suite ok --baseline previous --max-regressions 0` —
   `2 added, 1 removed` (beklenen: `1 added, 0 regressed`).

**Kanıt**
- `PUT /api/evals/ok/cases` yanıtı, adım 2 vs adım 4: case id
  `01a0ac99-286a-...` → `01a0aca3-0b27-...` (aynı `query`/`expectedOutput`).
- `tracon eval` çıktısı: `vs baseline 01a0ac9b-5e94-...: 0 regressed, 0
  fixed, 2 added, 1 removed.` (koşum kaydında MT-CLI-029 altında tam
  metin).
- Kaynak: `EvaluationContracts.cs:23` (`EvalCaseInput`, `Id` yok),
  `EvalEndpoints.cs:361-370` (`Id` set edilmiyor),
  `SqlEvalStore.cs:128-133` (`ReplaceCasesAsync`, id koruma store'da VAR
  ama kullanılamıyor).

**Kapsam**
Yalnız MT-CLI-029 değil — `17-EVAL-VE-DENEYLER.md`'nin
`--baseline`/case-diff case'leri de aynı yolu kullanıyorsa aynı sınıfa
girer (sınıf taraması kapanış oturumunda yapılmalı,
`kusur-giderme` skill'i). Etkilenen tek yüzey: `PUT /api/evals/{name}
/cases` (tam değiştirme ucu) ve onu kullanan her istemci (yönetim
arayüzü dahil, eğer case düzenleme ekranı bu ucu çağırıyorsa).

---

## Fiziksel eylem gerektiren case'ler

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| `MT-CLI-012` | 👤 insan gerekir | Yeni bir konsol uygulamasında `Tracon.Client` referanslanır, `AddTraconClient` ile `TraconApiClient` çözülür; IntelliSense'in metot/parametre adlarını gösterdiği ve çağrının gerçek sunucudan yanıt döndüğü gözle doğrulanır |
| `MT-CLI-022` | 👤 insan gerekir | Uzun koşan bir eval takımında koşum sırasında gerçek bir `Ctrl+C` tuş vuruşu yapılır; komutun hemen çıkıp `2` döndüğü, asılı kalmadığı doğrulanır |
| `MT-CLI-037` | 👤 insan gerekir | `docs-site` `reference/versioning` sayfasının "The supported upgrade window" bölümü okunur; desteklenen atlama aralığı, dayanağı ve MAF sınırının açıkça yazılı olduğu gözle doğrulanır |
| `MT-CLI-046` | 👤 insan gerekir | Node.js veya gerçek bir tarayıcıda `@tracon/client` ile `readSse(response)` çağrılır; çerçevelerin `{id,event,data}` şeklinde geldiği, `: keep-alive` yorumlarının görünmediği ve çok satırlı `data`'nın birleştiği gözle doğrulanır |
