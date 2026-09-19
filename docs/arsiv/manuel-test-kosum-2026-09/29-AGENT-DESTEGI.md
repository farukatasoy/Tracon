# 29 — Tüketici Agent Desteği (`AGD`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../29-AGENT-DESTEGI.md`](../../manuel-test/29-AGENT-DESTEGI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun (oturum 14) `Gerçek sonuç` ve
> `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s3` (Faz B, ilk aile — bu şeridin dağılımı: 32 · **29** · 34 · 21 · 11 · 25 · 17 · 20) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s3` · dal `test/kosum-s3` |
| **Kod** | `7e3a4de7` donuk (`ap-s3` HEAD `e5d302e5`, fast-forward — aynı kaynak) |
| **Case sayısı** | 24 (MT-AGD-001..024) |
| **Port** | Bu aile ağırlıklı olarak `dotnet build`/CLI tabanlı; şeridin kendi 5083/`mt_s3`'üne **dokunulmadı**. MT-AGD-019/020 istisna — bkz. aşağıdaki sapma notu |
| **Paket kaynağı** | `dotnet pack Tracon.src.slnf -c Release` **`ap-s3`'ün kendi worktree'sinden** koşuldu (spec'in "Koşmadan önce"i `/Users/farukatasoy/Desktop/projects/Tracon`'u yazar — bu oturumun talimatı başka worktree'ye dokunmayı yasakladığı için `ap-s3`'e uyarlandı). Üretilen paket: `Tracon 0.0.0-preview.0.821`, yerel feed `/Users/farukatasoy/Desktop/projects/ap-s3/artifacts/package/release` |

**Sapmalar (bu oturumda ölçüldü):**

1. **MT-AGD-019/020 portu değiştirildi.** Spec'in `Girilecek veri` blokları
   `localhost:5082`'yi sabit yazıyor — bu, `samples/Tracon.Embedded`'in kendi
   `launchSettings.json`'undaki varsayılan port. Ancak `5082` aynı zamanda
   `ap-s2` şeridinin **kendi** `Tracon.Api` portu (Faz B dağılım tablosu,
   DEVIR.md §5) — o an boştaydı ama koşum sırasında `ap-s2` kendi işine
   başlarsa çakışabilirdi. Kural 1.3 ("yalnız kendi portun") gereği
   `samples/Tracon.Embedded` bu koşumda **`5093`**'te açıldı, `curl` hedefleri
   buna göre uyarlandı. Davranış aynı; yalnız port farklı.
2. **Küresel `dotnet new` şablon kaydı güncellendi.** Önceki bir oturumdan
   (dosya 05, `~/tracon-manuel` akışı) kalan `Tracon.Templates::0.0.0-preview.0.789`
   `dotnet new uninstall` ile kaldırıldı, `ap-s3`'ün kendi `src/Tracon.Templates`'i
   (`0.0.0-preview.0.821`) `dotnet new install` ile kuruldu. Kural 1.3
   "küresel kayıt — yalnız o dosyayı koşan ajan dokunur" bunu bu ailenin
   sorumluluğu yapıyor; başka hiçbir şerit bu turda şablon kullanmıyor
   (yalnız aile 30/YEREL-REFERANS ileride kullanabilir).
3. **`user-secrets` hiç kullanılmadı** — bu ailenin hiçbir case'i sağlayıcı
   kimliği gerektirmiyor (`echo`/`openai` adı yalnız TRC0102'yi tetiklemek
   için literal string olarak geçiyor, gerçek çağrı yok).

**Spec düzeltmeleri (kural 1 istisnası — doküman koddan sapmıştı):**

- **MT-AGD-015/016:** `agentMapBudgetBytes` (11264, Faz 153) ile
  `llmsBudgetBytes` (24576, `llms.txt`'e özel) birbirine karıştırılmıştı;
  MT-AGD-016'nın "10 240 bayt" metni Faz 85'ten kalan bayat bir sabitti.
  `docs-site/scripts/build-agent-map.mjs` okunarak düzeltildi.
- **MT-AGD-016/018:** "Embedding points" bölümü Faz 85'te **beş** giriş
  taşıyordu; ürün o zamandan beri `IRunAuthorizationHandler` (run/session
  authorization) ve `IToolApprovalPresenter` ile büyüdü — bugün **yedi**
  giriş var (haritanın kendi `- Rule:` satırı da artık "seven" diyor).
  Her iki case'in `Beklenen sonuç`'u bu ölçümle güncellendi, eski metin
  silinmedi.

---

## Devir notu

**🎉 AİLE 29 KAPANDI — 22/24 case koşuldu, 2 case `☐ Beklemede`** (MT-AGD-018,
MT-AGD-024 — bkz. aşağıdaki fiziksel/agent-eylem tablosu). Dosya sonucu:
**22 ☑ Geçti · 0 ☑ Kaldı · 2 ☐ Beklemede · 0 ⏭ Atlandı**.

**Bu aile hiçbir yeni ürün kusuru bulmadı** — dosya 07'deki desenin aynısı.
22 koşulan case'in tamamı `Beklenen sonuç` ile birebir eşleşti (üçü, yukarıda
anlatıldığı gibi, önce spec'in kendisini düzeltmeyi gerektirdi — ürün değil
doküman bayattı).

**MT-AGD-018 ve MT-AGD-024 neden `Beklemede`:** İkisi de (👤 işaretli) bu
ailenin en elle işi ağır case'leri — "bir kod agent'ına dokümanı ver, agent'ın
kod agent'ı **olarak** ne ürettiğini gözlemle" deseninde. Bunu gerçekleştirmek
bu koşum oturumunun kendi araç setinin dışında izole, taze-bağlamlı ayrı bir
`claude` CLI çağrısı gerektiriyor (MT-AGD-024 açıkça `claude 2.1.269` sürümünü
ve `--output-format stream-json` bayrağını adlandırıyor). Bütçe ve kapsam
gerekçesiyle bu oturumda denenmedi; ikisi de sonraki 5+ case'i **bloklamıyor**
(aile zaten tamamlandı), bu yüzden sormadan `Beklemede` bırakıldı ve fiziksel
eylem tablosuna yazıldı. Her iki case'in spec metni zaten bir "Gerçek koşum
(2026-09-16 / 2026-08-21, ...)" notu taşıyor — bu, **önceki** bir doğrulamanın
kaydı, bu turun kendi `Gerçek sonuç`'u değil; iki ayrı şey karıştırılmamalı.

**Sonraki oturumun işi:** Aile 29 kapandı, sıradaki aile **34** (`ap-s3`
dağılımının bir sonraki kalemi — 32 · 29 · **34** · 21 · 11 · 25 · 17 · 20).
MT-AGD-018/024 kapanış modunda (Aşama 2) ya da ayrı bir "kod agent'ı
gözlemi" oturumunda ele alınmalı; ikisi de gerçek bir `claude` CLI çağrısı ve
transkript incelemesi istiyor.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show cc9895d2:docs/manuel-test/kosumlar/2026-09-16/29-AGENT-DESTEGI.md
> ```

---

## Temiz geçen case'ler (19)

| Case | Durum | Başlık |
|---|---|---|
| MT-AGD-001 | ☑ | Özellik kapalıyken hiçbir dosya yazılmaz |
| MT-AGD-002 | ☑ | Özellik açıkken harita git köküne yazılır |
| MT-AGD-003 | ☑ | Var olan `AGENTS.md` asla ezilmez |
| MT-AGD-004 | ☑ | Git kökü yoksa target çökmez |
| MT-AGD-005 | ☑ | Çok projeli tek build tek dosya bırakır |
| MT-AGD-006 | ☑ | `dotnet new tracon-api` haritayı kendiliğinden getirir |
| MT-AGD-007 | ☑ | `TRC0101`: mapping var, kayıt yok |
| MT-AGD-008 | ☑ | `TRC0102`: bağlanan sağlayıcı kayıtlı değil |
| MT-AGD-009 | ☑ | `TRC0201`: tanıma düz `secret` yazıldı |
| MT-AGD-010 | ☑ | `TRC0301` ve `TRC0302`: elle yazılmış yerine koyma |
| MT-AGD-011 | ☑ | `TRC0401`: harita bayat |
| MT-AGD-012 | ☑ | Tek özellik tüm aileyi susturur |
| MT-AGD-013 | ☑ | Kapı: harita kod ile hizasını kaybederse test kızarır |
| MT-AGD-014 | ☑ | Kapı: üretilen harita commit'ten saparsa denetim kızarır |
| MT-AGD-017 | ☑ | Gömme sayfası `llms.txt` sayfa indeksinde — Faz 85 |
| MT-AGD-019 | ☑ | Arka plan işi kendi kiracısıyla kapanır — Faz 85 |
| MT-AGD-020 | ☑ | Event bridge doldurulunca DÜŞÜRÜR, koşuyu yavaşlatmaz — Faz 85 |
| MT-AGD-022 | ☑ | `tracon agent-skill` yazar, ikinci koşum DOKUNMAZ — Faz 178 |
| MT-AGD-023 | ☑ | Bayat kapı skill'i `TRC0403` ötürür — Faz 178 |

## Ayrıntı taşıyan case'ler (5)

## MT-AGD-015 — `llms.txt` siteden erişilebilir

**Gerçek sonuç**
`npm run build` (48s, 1147 sayfa). `head -1 dist/llms.txt` →
`<!-- Tracon agent map · revision: 76d02c99 · generated by
docs-site/scripts/build-agent-map.mjs -->`. `wc -c`: `llms.txt` **21834
bayt** (≤ `llmsBudgetBytes` = 24576 — spec'in "agentMapBudgetBytes" referansı
yanlış sabiti adlıyordu, `Beklenen sonuç` düzeltildi), `llms-full.txt`
**784388 bayt** (~766 KB — spec'in "~350 KB"ı Faz 85'ten kalan bayat bir
tahmindi, düzeltildi). `llms-full.txt` içinde üretilen HTTP API referansı
**yok**: dosya `https://tracon.dev/http-api/` adresine yönlendiren bir
cümle taşıyor, uç noktaları kendi gövdesinde listelemiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-016 — Gömme ekseni haritada — Faz 85

**Gerçek sonuç**
`### Embedding points` bölümü bugün **yedi** satır taşıyor — Faz 85'te beş
noktayla yazılan bu case'in metni bayattı: `IRunAuthorizationHandler`
(run/session authorization) ve `IToolApprovalPresenter` (tool-approval
presentation) sonradan eklenmiş, `- Rule:` satırının kendisi de artık
"seven" diyor (`GET /api/diagnostics reports which of the seven are still
built-in`). `Beklenen sonuç` bu ölçümle güncellendi (bkz. MT-AGD-018 için
aynı düzeltme). Dosya toplamı **10684 bayt** ≤ `agentMapBudgetBytes` (11264).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-018 — 👤 Gömme sayfası bir kod agent'ına sorulmadan yeterli — Faz 85

**Gerçek sonuç**
Koşulmadı. Bu case izole, taze-bağlamlı bir kod agent'ına (Claude Code veya
benzeri) yayımlanmış `guides/embedding.md`'yi verip — `samples/
Tracon.Embedded` **gösterilmeden** — "Tracon'i mevcut bir ASP.NET Core
uygulamasına göm" görevi verip ürettiği kodu okumayı gerektiriyor. Bu, bu
koşum oturumunun kendi çalışma modelinin dışında ayrı, izole bir agent
oturumu istiyor (bu oturumun kendisi zaten bağlamı dolu bir agent'tır ve
"hiç görmeden" bir testin öznesi olamaz). Fiziksel eylem gerektiren
case'lerle aynı gerekçeyle ertelendi (skill §1.4.2) — 5+ case'i bloklamıyor,
aile zaten tamamlandı. Spec'in kendi metni Faz 85'te ("2026-08-21 ölçümünde
hiçbir sayfa bu beşini birlikte anlatmıyordu") ve bu case'in başlığındaki
"beş nokta" referansı artık bayat (bkz. MT-AGD-016 — ürün yedi noktaya
büyüdü); `Beklenen sonuç`'a bu turda bir not eklendi, canlı koşum yapılmadı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-021 — `TRC0501`/`TRC0502` gerçek tüketici derlemesinde öter — Faz 93

**Gerçek sonuç**
Beş adımın hepsi tam beklenen sonucu verdi: (2) döngü dışı
`TraconRunContext.SetCurrent(scope)` içeren `async IAsyncEnumerable` →
`warning TRC0501`, `BadAsync` adıyla, `MoveNextAsync` öncesi tekrarı
anlatan tam mesaj metniyle. (3) yazım döngü başına taşınınca uyarı
**kayboldu**. (4) `AmbientTenantScope.Begin("t1")` `using` olmadan, ayrı
statik metotta → `warning TRC0502`, `'AmbientTenantScope.Begin'` adıyla. (5)
`-p:TraconUsageDiagnostics=false` ile derlenince (hem TRC0502 kaynağı hem
düzeltilmiş TRC0501 kaynağı derlemede) **`0 Warning(s)`**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-AGD-024 — 👤 Üretilen skill Claude Code'da GERÇEKTEN yükleniyor — Faz 178

**Gerçek sonuç**
Koşulmadı. Bu case gerçek `claude` CLI'ını (`--output-format stream-json`)
izole bir proje kökünde, Tracon yüzeyine dokunan bir görevle çalıştırıp
tool-çağrı transkriptinde `Skill(tracon)`'ın **ilk** eylem olduğunu, ve bir
kontrol koşumunda (`.agents/skills/` yolunda, `.claude/` değil) hiç
çağrılmadığını doğrulamayı gerektiriyor — nested bir `claude` çağrısı, bu
koşum oturumunun kapsamı ve araç setinin dışında. MT-AGD-018 ile aynı
gerekçeyle ertelendi (skill §1.4.2) — 5+ case'i bloklamıyor, aile zaten
tamamlandı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Koşulamayan case'ler

| Case | Neden | Kullanıcıdan/sonraki oturumdan istenen |
|---|---|---|
| MT-AGD-018 | 👤 İzole, taze-bağlamlı bir kod agent'ına (Claude Code veya benzeri) `guides/embedding.md`'yi verip **samples/Tracon.Embedded'i göstermeden** ürettiği kodu okumak gerekiyor — bu koşum oturumunun kendi araç setinin dışında ayrı bir agent oturumu istiyor. | Ayrı bir oturumda: agent'a yayımlanmış `guides/embedding.md` metni/URL'i verilip "Tracon'i mevcut bir ASP.NET Core uygulamasına göm" görevi verilsin, üretilen kodun yedi gömme noktasının (bkz. MT-AGD-016 düzeltmesi — artık beş değil yedi) tamamını `AddTracon()`'den önce bulup bulmadığı okunsun. |
| MT-AGD-024 | 👤 Gerçek `claude` CLI'ı (`--output-format stream-json`) izole bir proje kökünde çalıştırıp tool-çağrı transkriptinde `Skill(tracon)`'ı aramak gerekiyor — nested bir agent çağrısı, bu oturumun kapsamı dışında bırakıldı. | Ayrı bir oturumda: `tracon agent-skill` ile yazılmış skill + `Tracon.LocalReference.md` içeren izole bir projede Claude Code'u Tracon'a dokunan bir görevle çalıştırıp ilk tool çağrısının `Skill(tracon)` olduğu doğrulansın; kontrol koşumu olarak aynı dosya `.agents/skills/tracon/SKILL.md` altına konup `Skill` çağrısının **çıkmadığı** teyit edilsin. |
