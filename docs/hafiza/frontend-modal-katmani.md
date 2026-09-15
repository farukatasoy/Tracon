# Arayuz Modal Katmani Tuzaklari

> `dialog.tsx` (odak tuzagi), `confirm-dialog.tsx` (dogrulama adimi), geri
> alinamaz aksiyon olcutu ve `window.confirm` yasagi.
>
> Kardes dosyalar: [`frontend-tasarim-katmani.md`](frontend-tasarim-katmani.md)
> (token, tema, yerlesim, diger primitifler) ·
> [`frontend.md`](frontend.md) (Vite, SPA rota, ekran mantigi) ·
> [`frontend-yerellestirme.md`](frontend-yerellestirme.md) (`useT`, sozluk).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken
> okunur. Faz 175'te ayrildi: `frontend-tasarim-katmani.md` %4 bosluga
> dusmustu ve modal katmani kendi basina tutarli bir eksendir — iki kapisi,
> bir olcutu ve kendi E2E kumesi var. K-214 merdiveni: gercek bolunme.

## Modal katmani ve dogrulama adimi

- **Modal katmanı TEK yerdedir** (Faz 164): `dialog.tsx`. Dört şeyi birden
  yapar (odağı içeri al · `Tab`'ı hapset · `Esc` · odağı tetikleyiciye geri
  ver) ve `Esc`'te `stopPropagation` çağırır — aksi hâlde kabuğun genel `Esc`
  bağlaması alttaki katmanı da kapatır. Komut paleti de bu hook'u kullanır.
  Kendi `fixed inset-0` panelini çizen bir ekran bir kusurdur. Faz 175'te bu
  cümle bir **kapıya** dönüştü: `scripts/check-modal-layer.mjs`. Kural allowlist
  DEĞİL — `command-palette.tsx` meşru olarak kendi backdrop'ını çizer ve doğru
  olmasının sebebi `useFocusTrap` koşmasıdır; kapı da tam olarak onu ister.
- **🚨 `window.confirm` bir doğrulama adımı DEĞİLDİR** (2026-09-16, Faz 175,
  K-785): konsolda **dokuz** çağrı yeri vardı ve üç şeyi birden bozuyordu.
  (1) Tarayıcının kendi dilinde gelir, `locale`'i tanımaz. (2) **Varsayılan
  düğmesi KABUL edendir** — yani kazara `Enter` silme yapar; erişilebilir bir
  doğrulamada odak İPTAL'de olmalıdır. (3) Olay döngüsünü bloke eder ve
  **Playwright onu otomatik dismiss eder** — bu yüzden bugüne dek hiçbir E2E
  olgusu bir silme düğmesine tıklayamıyordu ve faz planı "tıklayan her olgu
  taşınır" derken taşınacak olgu yoktu. Sayı artık sıfırdır; `alert`/`prompt`
  de yasaktır (mesaj `ErrorNote`'a, girdi bir forma gider).
- **🚨 Doğrulama adımı bir ÖLÇÜTE bağlıdır, tercihe değil** (2026-09-16,
  Faz 175, K-786): aksiyon dialog alır ancak (a) arayüzden aynı girdilerle geri
  getirilemeyen bir durumu yok ediyorsa **veya** (b) tekrarlanamayan bir kararı
  kesinleştiriyorsa. Ölçüt **ölçülür**: `grep -rn "ON DELETE"
  src/Tracon.PostgreSql/Migrations/*.sql`. `CASCADE` varsa (a); `SET NULL` veya
  bağımlı tablo yoksa yoktur. K-059 gereği `secret` satırda hiç durmadığı için
  tetikleyici ve MCP kaydı silmesi ölçütü **geçmez**. En ince vaka
  `experiments`: `runs.experiment_id` FK taşımaz, yani satırlar kalır — ama
  varyant eşlemesini yalnız deney satırı taşır ve aynı adla yeniden oluşturmak
  YENİ bir `uuid` verir, dolayısıyla sonuçlar okunamaz hâle gelir → (a).
  Ölçüt dışı bir dialog `faz-denetim` bulgusudur; bir E2E olgusu da tutar
  (`An_action_that_fails_the_criterion_stays_one_click...`).
- **`Dialog`'un varsayılan açılış odağı KAPATMA düğmesidir, ilk gerçek cevap
  değil** (2026-09-16, Faz 175): `useFocusTrap` DOM sırasındaki ilk
  odaklanabilir öğeye odaklanır ve `Dialog`'da o öğe başlıktaki `✕`'tir.
  Doğrulamada odak `İptal`de olmalıdır; bunu ebeveyn-çocuk effect sırasına
  yaslanarak sonradan taşımak **çalışır ve bir sonraki render sırası
  değişikliğinde sessizce bozulur**. `initialFocus` prop'u sözleşmeyi açık yapar.
- **🚨 `busy` çift tıkı DURDURMAZ** (2026-09-16, Faz 175): `busy` ancak
  çağıranın mutation'ı yeniden render ettiğinde `true` olur ve iki tık tek bir
  frame'e sığar. Silme ucunda bu iki istek, `approvals/{id}/decide` ucunda artık
  var olmayan bir isteğe ikinci bir hüküm demektir. Koruma `useRef` ile **tık
  işleyicisinin içinde senkron** olmalıdır (`ConfirmDialog`'daki `fired`).
- **Bir dialog başlığına bir kayıt adı koymak mevcut E2E olgusunu kırar**
  (2026-09-16, Faz 175): "Approve cancel_order?" başlığı `GetByText("cancel_order")`
  ile eşleşti ve Faz 165'in kayıtlı strict-mode sınıfını tekrarladı. Olgu
  gevşetilmedi, `Exact = true` ile daraltıldı. Başlığa parametre koyarken
  `grep -rn '<değer>' tests/Tracon.Ui.E2ETests/` ile önceden tara.
