# Faz 175 — Geri Alınamaz Karar Doğrulaması

> **Durum:** ✅ Tamamlandı (2026-09-16)
> **Plan onayı:** onaylandı (2026-09-16, kullanıcı) — dört açık sorunun dördü de
> önerilen seçenekle kapandı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-224**
> **Önkoşul:** [Faz 164](164-CONSOLE-ENSTRUMAN-KATMANI.md) — `Dialog` primitifini ve `Tooltip` sonuç-bildirim desenini o faz kurdu
> **Paketler:** `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — değişiklik `Tracon.UI` frontend'inde, sevk edilen .NET yüzeyinde değil
> **Tüketici yüzeyi:** `docs-site/src/content/docs/ui.md` (plan `guides/ui.md` yazıyordu; o yol YOKTUR) · sevk edilen: `locales/{en,tr}/*.ts` yeni anahtarlar
> **Manuel test alanı:** `docs/manuel-test/09-ARAYUZ-GENEL.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 4406e07a:docs/arsiv/fazlar/175-GERI-ALINAMAZ-KARAR-DOGRULAMASI.md
> ```
>
> Damıtıldı 2026-09-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Onaylar ekranında bir tool çağrısını onaylamak veya reddetmek **tek tıktır** ve geri alınamaz. K-368 bunu kesinleştirir: karar verildikten sonra aynı `RunId` devam etmez. İki düğme yan yanadır. Aynı desen on bir yerde daha yaşıyor.

## Bitiş Ölçütleri (DoD)

- [x] §175.3 tablosunun her satırı ölçütle sınandı ve karar **bu dokümana** yazıldı — "Ölçütün Şema Kanıtıyla Sınanması" tablosu; her satırın gerekçesi bir `ON DELETE` kuralıdır, tahmin değil. Plan sınıflandırması **aynen doğrulandı**, hiçbir satır düşmedi
- [x] Ölçütü geçen her aksiyon `ConfirmDialog` kullanıyor; geçmeyen **hiçbiri** kullanmıyor — tam 6 kullanım, denetçi bağımsız doğruladı. Ölçütü geçen dört ekranın `onRetry` yolu da (denetim 🟡 #2) artık doğrulamayı yeniden açıyor, silmeyi tetiklemiyor
- [x] `Esc` ve `İptal` hiçbir istek atmadan kapatıyor — `Escape_and_cancel_close_the_confirmation_without_any_request` `DELETE` sayısını sayar ve **0** bekler. Örnek uygulamada da doğrulandı (`Esc` sonrası `GET /api/sessions` hâlâ `['faz175-manuel']`)
- [x] Onay düğmesi varsayılan odakta **değil** — `Confirm_does_not_default_focus_the_destructive_button`; ayrıca gerçek tarayıcı anlık görüntüsünde `Cancel` `[active]`
- [x] Etkilenen E2E olguları aynı commit'te taşındı; olgu sayısı yazıldı — `UiTests.cs` **70 → 78**, proje **71 → 79**. Taşınan tek olgu `Approvals_screen_…` ve **gevşetilmedi**
- [x] `en` ve `tr` eksiksiz; `npm run build` temiz — 16 anahtar iki dile birlikte eklendi, 4 ölü anahtar silindi; `Pick<Messages, …>` eksik anahtarı derleme hatası yapar (K-228)
- [x] Bundle payı **gzip KB olarak** ölçüldü ve yazıldı — **190,5 → 192,4 KB** (+1,9 KB), 250 KB bütçesinin altında. Taban temiz bir `git worktree` içinde ölçüldü
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 54eea002`
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `kapi.py tarama`: "✅ temiz (6 işaretli sentetik credential atlandı)"
- [x] Manuel kabul case'leri eklendi; otomatikleştirilebilenler koşuldu — `09-ARAYUZ-GENEL.md` MT-UI-055…057, üçünün de otomatik karşılığı yeşil. Ayrıca **yedi bayat case** yeni akışa taşındı (denetim 🟡 #4 + sınıf taraması)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki 🔴 bulundu, ikisi de kapandı ve her kapanış düşen bir testle kanıtlandı ("Denetim Bulguları")
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz — `ui.md`: onay akışı, ölçüt anlatısı, `confirm()` notu ve güncel bundle sayısı

### Örnek uygulamayla gerçek koşum

`dotnet run --project samples/Tracon.Api -c Release`, `http://localhost:5000/tracon`.

| Adım | Sonuç |
|---|---|
| `POST /api/agents/support/run` (`sessionId=faz175-manuel`) | `200`, SSE akışı, `runId=01a0a709-…` |
| Oturumlar ekranı, silme düğmesine **odak** | Tooltip: "Deletes the conversation and every message in it. The runs it started keep their own rows…" |
| Silme düğmesine tık | Konsolun kendi dialogu açıldı; erişilebilirlik ağacı `button "Cancel" [active]` — açılış odağı İptal'de |
| `Esc` | Dialog kapandı, `GET /api/sessions` hâlâ `['faz175-manuel']` — **hiçbir istek gitmedi** — ve odak `button "Delete session" [active]` ile tetikleyiciye döndü |
| Tekrar aç → `Delete` | `GET /api/sessions` → `[]`, `GET /api/runs` → **1 run** hâlâ kayıtlı. Etki cümlesinin vaadi birebir gerçekleşti |
| Jobs ekranı (düzeltme öncesi bundle) | 🚨 Sızan yorum aksiyon hücresinde **basılı görüldü** — denetim 🔴 #1'in canlı kanıtı |
| Jobs ekranı (düzeltme sonrası bundle) | Hücrede yalnız `Trigger` · `Edit` · `Delete this schedule` ve etki tooltip'i; metin yok |

### Doğrulama komutları

```bash
# E2E olgu sayısı ve geçiş
dotnet test tests/Tracon.Ui.E2ETests

# i18n bütünlüğü ve bundle
cd src/Tracon.UI/frontend && npm run build
gzip -c ../wwwroot/assets/index-*.js | wc -c   # gzip payı
```

---

## Plandan Sapmalar

> Plan doğru bir ölçüt yazdı ve kanıt tablosunda üç yerde bayattı. Üçü de burada.

**1. 🚨 Planın en büyük iddiası yanlıştı: doğrulama adımı YOKTU değil, DOKUZ
yerde `window.confirm` VARDI.** §175.2 tablosu katman 2 için "Hiçbiri için yok"
diyor. `grep -rn "window\.confirm" src/Tracon.UI/frontend/src/` dokuz çağrı yeri
gösterdi: `agent-detail` · `sessions` · `evals` · `experiments` · `skill-editor` ·
`jobs` · `triggers` · `mcp` · `cancel-run-button`. Faz bu yüzden "olmayan bir
adımı eklemek" değil, **erişilebilir olmayan bir adımı değiştirmek** oldu.

Bu bir ayrıntı değil, fazın gerekçesini güçlendiren bir bulgudur. Native
`confirm()` üç şeyi birden yapamaz: biçimlenemez (tarayıcının kendi dilinde
gelir, konsolun `locale`'ini tanımaz), **varsayılan düğmesi KABUL edendir** —
yani §175.4 kural 1'in önlemek için var olduğu tam mis-click — ve olay döngüsünü
bloke eder. Sonuncusu §175.5'in sessiz cevabıdır: **Playwright native dialog'u
otomatik dismiss eder**, dolayısıyla bugüne kadar hiçbir E2E olgusu bir silme
düğmesine tıklayamıyordu. Plan "silme düğmesine tıklayan her olgu taşınır"
diyordu; tıklayan olgu **yoktu**. Taşınan tek olgu onay ekranınınkiydi.

Kullanıcı kararı (2026-09-16): ölçüt harfiyen uygulanır. Ölçütü geçmeyen üçün
(`jobs.remove`, `triggers.remove`, `mcp.remove`) `window.confirm`'i **kaldırıldı**
ve yerine katman 1 kondu. Konsolda `window.confirm` sayısı **sıfırdır** ve bir
kapı bunu zorlar (aşağıda).

**2. `cancel-run-button.tsx` envanterde hiç yoktu ve `window.confirm` taşıyordu.**
§175.1 iptali "yok edici değil" diye dışarıda bırakmış ama o dosyaya hiç
bakmamıştı. Kullanıcı kararı: ölçütü geçmiyor → `window.confirm` kalktı,
`runDetail.cancel.confirm` bir etki cümlesine (`runDetail.cancel.effect`)
dönüştü ve `Tooltip`'e taşındı. Envanter 12 değil **13** kalemdir.

**3. E2E olgu sayısı ve bundle tabanı ikisi de bayattı.** Plan "olgu sayısı 71"
diyor ve `UiTests.cs`'i gösteriyor; o dosyada **70** olgu vardır, 71 projenin
toplamıdır (`DocumentationScreenshotTests` bir olgu taşır). Bundle tabanı olarak
`160 188 B` brotli yazılmış; temiz bir worktree'de (`git worktree add --detach
HEAD`) ölçülen taban **162,6 KB brotli / 190,5 KB gzip**'tir.

**4. `ConfirmDialog` planlanan imzaya iki alan ekledi: `error` ve `Dialog`'a
`initialFocus`.** İkisinin de gerekçesi plandaki hata modu tablosundadır.

- `error`: tablo `Server_refusal_is_shown_in_the_dialog` istiyor ama §175.4'ün
  imzasında hata alanı yok. Dialog aksiyon uçarken **açık kalır** ve reddi
  içinde gösterir; modal olduğu için ekranın kendi `ErrorNote`'u arkada kalır ve
  operatör aynı cümleyi iki kez görmez.
- `initialFocus`: `useFocusTrap` DOM sırasındaki ilk odaklanabilir öğeye
  odaklanıyordu; `Dialog`'da o öğe başlıktaki **kapatma düğmesidir**, `İptal`
  değil. Ebeveyn-çocuk effect sırasına yaslanıp sonradan odak taşımak çalışırdı
  ve bir sonraki render sırası değişikliğinde sessizce bozulurdu. Primitife
  isteğe bağlı bir `initialFocus` eklemek sözleşmeyi açık yapar.

**5. `Button` bir `ref` prop'u aldı.** `ConfirmDialog`'un `İptal` düğmesine
odaklanabilmesi için bir ref gerekiyordu. İlk uygulama düğmeyi elle yazdı ve
`CONTROL_BASE` + `CONTROL_TONES.default` kopyası üretti — Faz 165'in kapattığı
sınıfın ta kendisi ("ayrı yazılırsa 1 px kayarlar"). React 19 `ref`'i sıradan
bir prop olarak geçirir, `forwardRef` gerekmez.

**6. Kapsam dışı bir kapı eklendi: `scripts/check-modal-layer.mjs`.** Dokuz
`window.confirm`'i temizlemek tek vakayı kapatır; kapı **sınıfı** kapatır
(Faz 93 deseni). İki kural zorlar: (a) `window.confirm|alert|prompt` yok, (b)
kendi `fixed inset-0` katmanını çizen her dosya `useFocusTrap` koşar. (b) için
allowlist yazmak kolay ve **yanlış** olurdu: `command-palette.tsx` meşru olarak
kendi backdrop'ını çizer ve doğru olmasının sebebi tam olarak `useFocusTrap`
koşmasıdır. Kapı `npm run build` zincirinde, `check-tokens.mjs`'den hemen
sonradır; kırmızı olduğu ölçüldü (`mcp.tsx`'e geçici bir `window.confirm`
kondu → `exit=1`).

**7. Ölçüt tablosu şema kanıtıyla sınandı ve plan sınıflandırması AYNEN
doğrulandı** (DoD 1. satırı). Tablo §175.3'ün altındadır; her satırın gerekçesi
artık bir tahmin değil, bir `ON DELETE` kuralıdır.

---

## Ölçütün Şema Kanıtıyla Sınanması

Her satır `grep -rn "ON DELETE" src/Tracon.PostgreSql/Migrations/*.sql` ile
sınandı. **Plan önerisinin tamamı doğrulandı; hiçbir satır düşmedi.**

| # | Aksiyon | Şema kanıtı | Ölçüt | Karar |
|---|---|---|---|---|
| 1 | `approvals.decide` | — (veri değil karar) | (b) K-368: soran `run` sonsuza dek `AwaitingApproval` | ✅ doğrulama |
| 2 | `agent-detail.remove` | `agent_definition_versions → agent_definitions ON DELETE CASCADE` | (a) sürüm geçmişi gider | ✅ doğrulama |
| 4 | `sessions.remove` | `conversation_items → conversations ON DELETE CASCADE` | (a) mesaj geçmişi gider | ✅ doğrulama |
| 5 | `evals.remove` | `eval_cases` **ve** `eval_runs → eval_suites ON DELETE CASCADE` | (a) case'ler ve puanlar gider | ✅ doğrulama |
| 6 | `experiments.remove` | `runs.experiment_id` FK **taşımaz** | (a) — satırlar kalır, **anlamları** gider: varyant eşlemesini yalnız deney satırı taşır ve aynı adla yeniden oluşturmak YENİ bir `uuid` verir | ✅ doğrulama |
| 11 | `skill-editor.remove` | `skill_scripts` + bağlar `→ agent_skills ON DELETE CASCADE` | (a) script gövdeleri başka yerde yok | ✅ doğrulama |
| 3 | `agent-detail.rollback` | — | ikisi de değil | ❌ katman 1 (Faz 164'ten beri var) |
| 7 | `jobs.remove` | `jobs.schedule_id → job_schedules ON DELETE **SET NULL**` | ikisi de değil — job geçmişi kalır | ❌ katman 1 eklendi |
| 8 | `triggers.remove` | `inbound_triggers` yalnız `signing_secret_configuration_name` tutar (K-059) | ikisi de değil | ❌ katman 1 eklendi |
| 9 | `mcp.remove` | `mcp_servers` yalnız `authorization_configuration_key` tutar (K-059) | ikisi de değil | ❌ katman 1 (metni etki cümlesine çevrildi) |
| 10 | `mcp.removeRule` | bağımlı tablo yok | ikisi de değil | ❌ katman 1 eklendi |
| 12 | `script-grants.revoke` | bağımlı tablo yok | ikisi de değil | ❌ katman 1 eklendi |
| **13** | `cancel-run-button` (**plan dışı**) | — | ikisi de değil — iş yeniden tetiklenir | ❌ katman 1 eklendi |

Sonuç: **6 doğrulama · 6 yeni/düzeltilmiş sonuç bildirimi · 1 zaten kapsanmış**.

---

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-785 — Konsolda `window.confirm` / `alert` / `prompt` KULLANILMAZ; tek bir modal katmanı vardır (`components/dialog.tsx`) ve `scripts/check-modal-layer.mjs` bunu zorlar** | Native dialog biçimlenemez (tarayıcının dilinde gelir, `locale`'i tanımaz), varsayılan düğmesi KABUL edendir (§175.4 kural 1'in önlediği mis-click), ve olay döngüsünü bloke eder — Playwright onu otomatik dismiss ettiği için hiçbir E2E olgusu bir silme düğmesine tıklayamıyordu. Faz 175 dokuz çağrı yeri buldu ve sıfıra indirdi. Kapı ayrıca kendi `fixed inset-0` katmanını çizen her dosyanın `useFocusTrap` koşmasını ister; allowlist DEĞİL, çünkü `command-palette.tsx` meşru olarak kendi backdrop'ını çizer ve doğruluğunun sebebi hook'u koşmasıdır. |
| **K-786 — Bir aksiyon doğrulama adımı alır ancak ve ancak (a) arayüzden aynı girdilerle geri getirilemeyen bir durumu yok ediyorsa VEYA (b) tekrarlanamayan bir kararı kesinleştiriyorsa; ölçüt şema kanıtıyla sınanır** | Onay yorgunluğu gerçek bir maliyettir: her şeyi doğrulatmak hiçbirini doğrulatmamakla aynı yere çıkar. Ölçüt bir tercih değil bir **ölçüm**dür — `ON DELETE CASCADE` varsa (a), `ON DELETE SET NULL` veya bağımlı tablo yoksa değil. K-059 gereği `secret` satırda hiç durmadığı için tetikleyici ve MCP kaydı ölçütü geçmez. Ölçüt dışı bir dialog `faz-denetim` bulgusudur ve `UiTests.An_action_that_fails_the_criterion_stays_one_click_and_still_says_what_it_does` onu bir olgu olarak da tutar. |
| **K-787 — Doğrulama dialogu aksiyon uçarken AÇIK KALIR ve sunucu reddini kendi içinde gösterir; `consequence` metni katman 1'in tooltip'iyle AYNI anahtardan gelir** | Reddedilince kapanan bir dialog operatöre başarıyla birebir aynı görünen bir ekran bırakır. Dialog modal olduğu için ekranın kendi `ErrorNote`'u arkada kalır ve aynı cümle iki kez görünmez. Tek anahtar kuralı `rollbackEffect` emsalinin tersidir: iki yerde iki cümle yazmak, aceleci bir operatörün hangisine baktığına bağlı bir kayma üretir. |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan onaylandı; 4 açık soru + 2 sapma sorusu kullanıcıya soruldu) |
| Düzeltme turu sayısı | 3 (JSX yorum konumu · analyzer MA0006/MA0002 + `NoWaitAfter` · `tr-TR` locale ile İngilizce başlık arayan yardımcı) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | `faz-denetim` bölümüne bakınız |
| Fazın ürettiği regresyon | 1 — `Approvals_screen_...` strict mode ihlali (dialog başlığı `cancel_order` içeriyor). Faz 165'in kayıtlı sınıfı; olgu gevşetilmedi, `Exact = true` ile daraltıldı |
| Faz kapandıktan sonra bulunan kusur | — |

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir denetçiyle koşuldu (2026-09-16). **İki 🔴, altı
🟡, üç 🟢.** Her 🔴 kapandı ve her kapanış **düşen bir testle** kanıtlandı.

### 🔴 1 — Bir kaynak yorumu SEVK EDİLEN METİN olarak bundle'a girdi

Bu fazın kendi ürettiği kusur. `{/* … */}` bir JSX yorumudur; **çocuk
konumunda** parantezleri düşürmek aynı karakterleri bir metin düğümüne çevirir.
`jobs.tsx`'te tam olarak bu oldu ve bir düğmenin neden doğrulama adımı
taşımadığını anlatan 300 karakterlik not, zamanlama satırının aksiyon
hücresinde **basıldı**.

Hiçbir kapı yakalamadı: `tsc` iki biçimi de geçerli sayar, o ekranın bileşen
testi yok ve hiçbir E2E olgusu oraya uğramıyor. Denetçi bundle'ı çözerek buldu.

**Düzeltme + kapı.** Yorum parantezlerine geri kondu ve sınıf
`scripts/postbuild.mjs` içindeki `rejectLeakedComments` ile kapatıldı: kapı
**derleme çıktısında** koşar, çünkü iki biçim ancak orada ayrışır — gerçek bir
yorum kaybolmuştur, sızmış olan ise düz bir string literal'dir. Kaynak
düzeyinde ayırt etmek "bu satır ifade konumunda mı çocuk konumunda mı"
sorusunu gerektirir; o bir parser'ın işidir, regex'in değil. Kırmızı olduğu
ölçüldü (parantezler kaldırıldı → derleme hata verdi).

### 🔴 2 — Sunucu reddinden sonra onay düğmesi ÖLÜ kalıyordu

Çift tık koruması (`fired` ref'i) yalnız dialog **kapandığında** sıfırlanıyordu.
Ama dialog bir reddi göstermek için bilerek **açık kalır**: 409'dan sonra
`busy` yine `false` olduğu için düğme etkin görünüyor, basıldığında hiçbir
istek gitmiyor ve hiçbir geri bildirim üretmiyordu — operatörün tek çıkışı
geri çekilmekti.

**Düzeltme.** Guard'ın koruduğu şey "**uçuşta** olan tek aksiyon"dur, o yüzden
aksiyon **sonuçlandığında** bırakılmalıdır. `busy`'nin düşen kenarı izleniyor.
Mevcut olgu (`A_refused_action_…`) bu yolu tam da atlıyordu: reddi görüyor,
sonra yalnız `İptal`e basıyordu. Olgu genişletildi — ikinci `Onayla` basışı
sunucuya ulaşmalıdır. Guard bırakma bloğu silinip test kırmızıya düşürülerek
kanıtlandı.

### 🟡 — beşi kapandı, biri gerekçelendi

| # | Bulgu | Ne yapıldı |
|---|---|---|
| 1 | Çift tık olgusu `fired` ref'ini **ayırt etmiyordu**: iki ayrı Playwright tıklaması arasında React yeniden çizer, `busy` düğmeyi `disabled` yapar ve `disabled` bir `<button>` olay göndermez — guard silinse de olgu yeşil kalıyordu | Olgu tek bir `EvaluateAsync` içinde `element.click(); element.click();` çağırıyor: React yeniden çizmeden işleyiciye **iki kez** girilir. Guard silinerek kırmızıya düşürüldü, geri konarak yeşile |
| 2 | Ölçütü **geçen** dört ekranın `ErrorNote onRetry`'si doğrulamayı **atlayarak** tek tıkla siliyordu — DoD ile doğrudan çelişki | Dördü de (`sessions`, `evals`, `experiments`, `skill-editor`) artık doğrulamayı **yeniden açıyor**. `A_refused_action_…` olgusu bunu da sayıyor |
| 3 | Belgedeki olgu sayısı yanlıştı (77/78 yazılmıştı) | Ölçüldü: `UiTests.cs` **70 → 78**, proje **71 → 79**, **8** yeni olgu |
| 4 | Kaldırılan `window.confirm` akışının manuel kabul setinde çağıranı kalmıştı | **Sınıf tarandı** (denetimin bulduğu bir case değil, **yedi** yer): `MT-UIAG-019` · `MT-UIAG-024` · `MT-UIRUN-021` · `MT-UIRUN-022` · `MT-UIRUN-037` · `MT-EVAL-015` · `00-INDEKS` asimetri notu · `21-DAYANIKLILIK` kapsam tablosu. Hepsi yeni akışa taşındı |
| 5 | DoD satırlarının hiçbiri işaretlenmemişti | Kapanışta kanıtlarıyla işaretlendi |
| 6 | `check-modal-layer.mjs`'in backdrop kuralı `useFocusTrap` **adını** arıyordu — hook'tan bahseden bir yorum kapıyı yeşile çeviriyordu; ayrıca `globalThis.confirm(…)` ve `window['confirm'](../../…)` biçimleri görünmüyordu | Kural artık **çağrıyı** arıyor (`useFocusTrap(`); regex `globalThis`/`self` öneklerini ve köşeli parantez biçimini de kapsıyor. `window["confirm"](../../…)` ile kırmızıya düşürüldüğü ölçüldü |

### 🟢 — aday listesine

1. `busy` iken `Esc` ve backdrop dialogu kapatıyor; sözleşme metni "both answers
   lock" diyor ve bu yalnız iki düğme için geçerli. Yönü güvenlidir (istek yine
   uçar, hata ekranın kendi notunda görünür).
2. Arayüz payı tablosunda gzip sütunu yalnız `index-*.js`, brotli sütunu
   js+css kapsıyor. İki sayı da doğru ölçülmüş; kapsamları farklı.
3. `The_whole_confirmation_is_reachable_…` ikinci `Tab` durağını indeksle değil
   `ShouldContain` ile sınıyor. Sıra zaten `stops[0]` ve `stops[3]` ile sabit.

**Denetçinin temiz bulduğu başlıklar:** ölçüt uygulaması (6 dialog, tam olarak
K-786 tablosuyla örtüşüyor) · E2E gevşetmesi **yok** · kaldırılan
`window.confirm`'lerde gizlenmiş güvenlik ağı kaybı **yok** · i18n eksiksiz ·
imza-gövde kayması yok · sevk edilen .NET yüzeyi büyümedi · `secret` yazımı yok.

## Sonraki Faza Devir Notu

**Konsolda tek bir modal katmanı var ve iki kapı onu koruyor.** `window.confirm`
sayısı sıfırdır ve `frontend/scripts/check-modal-layer.mjs` geri gelmesini
engeller; `scripts/postbuild.mjs` ise bir kaynak yorumunun sevk edilen metne
dönüşmesini engeller. İkisi de `npm run build` zincirindedir.

**Yeni bir yıkıcı düğme eklerken önce ölçütü koş.** K-786: aksiyon
`ConfirmDialog` alır ancak (a) arayüzden geri getirilemeyen bir durumu yok
ediyorsa veya (b) tekrarlanamayan bir kararı kesinleştiriyorsa. Ölçüt tahmin
edilmez, **ölçülür**: `grep -rn "ON DELETE" src/Tracon.PostgreSql/Migrations/*.sql`.
`CASCADE` varsa (a) vardır. Ölçütü geçmeyen bir aksiyon **yine de** bir etki
cümlesi taşır — `Tooltip` ile, ve o cümle dialogunkiyle **aynı i18n
anahtarından** gelir.

**Bir davranış değiştiğinde manuel kabul setini `grep`'le.** Bu fazda
`window.confirm` kaldırılınca yedi manuel case bayatladı; denetim bunlardan
**birini** buldu, sınıf taraması altısını daha. Yeni bir kabul case'i yazmak
kolay kısımdır; bayatlayanı bulmak `grep -rn "<eski davranış>" docs/manuel-test/`
ile başlar.

**Açık kalan üç 🟢 aday** yukarıdadır; hiçbiri bir kusur değildir.
