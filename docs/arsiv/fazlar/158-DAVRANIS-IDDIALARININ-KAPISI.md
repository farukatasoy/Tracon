# Faz 158 — Davranış İddialarının Kapısı

> **Durum:** ✅ Tamamlandı (2026-09-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-171** (kalan yarısı)
> **Önkoşul:** Yok. Sayı yarısı ve sürüm damgası yarısı **kapandı** — aşağıya bak
> **Paketler:** Yok — kapı `docs-site/scripts/` ve `scripts/` içinde yaşar
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Doğrudan yok; kapı yanlış yazılmış tüketici sayfasını kırar
> **Manuel test alanı:** [`docs/manuel-test/32-DOKUMAN-KALITESI.md`](../../manuel-test/32-DOKUMAN-KALITESI.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 15ffc8e7:docs/arsiv/fazlar/158-DAVRANIS-IDDIALARININ-KAPISI.md
> ```
>
> Damıtıldı 2026-09-08 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Sevk edilen metindeki **sayılabilir** iddialar artık kapı altındadır. **Davranış** iddiaları değil. Bir sayı koddan yeniden hesaplanabilir; "varsayılan olarak kapalıdır" cümlesi hesaplanamaz — yalnız yeniden ölçülebilir. Bu faz o ölçümü otomatikleştirir.

## Bitiş Ölçütleri (DoD)

- [x] İşaretli iddiaların her biri koddaki gerçek değerle karşılaştırılıyor — plan dokuz diyordu, gerçekleşen **on** oldu (bkz. Plandan Sapmalar)
- [x] Kasten bozulan bir varsayılan cümlesinde kapı **kırmızı**; doğru cümlede yeşil — `TraconSchedulingOptions.RunWorker`'ın initializer'ı kaldırılıp elle doğrulandı (bkz. Denetim Bulguları, kanıt komutları aşağıda)
- [x] İşaretsiz davranış iddiası sayısı raporda **görünür** (sıfır olması gerekmez) — `node docs-site/scripts/check-content.mjs` çıktısı: `Behavior claims: 11 marked and verified by DocumentedPolicyTests.cs; 146 sentence(s) across manual pages match "by default" or "defaults to"`
- [x] Bir uç scope'u ile sayfanın yazdığı scope ayrıştığında fonksiyonel test kırmızı — `OpenAIChatCompletionsEndpoints`'in `RequireApiKeyScope`'u `RunsRead`'e çevrilip elle doğrulandı: `Every_marked_endpoint_policy_claim_is_actually_enforced` kırmızı, mesaj her iki değeri de yazdı
- [x] Kapı yanlış pozitif üretmiyor: mevcut 147 satırın hiçbiri işaretlenmeden kırmızı yapmıyor — `npm run check:content` işaretlemeden önceki taban çizgiye karşı yeşil kaldı
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 91004b3a` (test adımı ilk turda kaynak çekişmesiyle kırmızı oldu, izole yeniden koşum ve tam ikinci koşum yeşildi; `docs/hafiza/test-altyapisi.md`'deki bilinen sınıf)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bu faz üretim/çalışma anı davranışını değiştirmiyor (yalnız doküman metni ve bir doğrulama kapısı); örnek uygulama `dotnet build` + `dotnet run` ile ayağa kaldırıldı, `GET /tracon/api/meta` → `200` doğrulandı, regresyon yok. Bu fazın gerçek iddiası (scope allow/deny) `DocumentedPolicyTests.cs` içinde **gerçek bir ASP.NET Core host'a karşı gerçek HTTP isteğiyle** kanıtlanıyor — samples üzerinde tekrarlamak aynı kanıtı ikinci kez üretmek olurdu
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/32-DOKUMAN-KALITESI.md` içine eklendi ve **`00-INDEKS.md` sayımı güncellendi** — `MT-DKL-021`..`024`, sayaç 20 → 24
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bir 🔴 bulundu ve kapatıldı (bkz. Denetim Bulguları)

### Doğrulama komutları

```bash
# Kasten bozup kapıyı sına
node docs-site/scripts/check-content.mjs
python3 scripts/dokuman-bakim.py --denetle
```

---

## Plandan Sapmalar

- **Dokuz değil ON iddia işaretlendi.** Planın 158.3 tablosu "SessionOwnership
  (2) · Retention:Enabled (2) · Sqlite · Scheduling · McpServer · Endpoint ·
  Images · Egress" diyordu — bu, 2+2+6×1 = **10**'dur, plandaki "dokuz" sayısı
  bir toplama hatasıydı. Sekiz kategorinin hepsi işaretlendi (hiçbiri
  atlanmadı); SessionOwnership için `capabilities.md`'nin iki satırı (121,
  187), Retention:Enabled için `capabilities.md` + `configuration.md`.
- **Endpoint policy tarafı için `scripts/dokuman-bakim.py` değişmedi.**
  Plan "gerekiyorsa" diyordu — gerekmedi: policy claim'in şekil doğrulaması
  (scope adının `ApiKeyScope`'un üyesi olması) `check-content.mjs`'e eklendi,
  gerçek deny/allow kanıtı `DocumentedPolicyTests.cs`'e. Python tarafı bu
  fazda dokunulmayan bir dosya kaldı; `scripts/dokuman_bakim_test.py` da
  değişmedi.
- **Açık Soru 1, 2, 3 planın önerisiyle kapatıldı** — A (HTML yorumu, cümlenin
  yanında), B (reflection, gerçek nesne), A (policy tarafı bu turda dahil).
  Uygulama sırasında dördüncü, plan dışı bir kural eklendi: işaretli her
  cümle, marker'ın hemen ÖNÜNDE değeri backtick'li bir **görünür** literal
  olarak da tekrarlamak zorunda (bkz. Denetim Bulguları #1) — bu, planın
  öngörmediği ama denetimde ortaya çıkan üçüncü bir tutarlılık kuralıdır.

## Bu Fazda Verilen Kararlar

Yok. Bu faz bir geliştirme aparatı ekliyor (kapı + doğrulama testi); public
API, uyumluluk sözleşmesi, güvenlik/kiracı sınırı veya kalıcı veri kararı
içermiyor — karar defterine girecek bir kalem yok.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent ile koşuldu (git diff + DoD, taban
`91004b3a`).

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | Kapı marker'ın değerini yalnız KODA karşı doğruluyordu (`DocumentedPolicyTests.cs`), marker'ın yanındaki GÖRÜNÜR cümleyle aynı olduğunu hiçbir yer denetlemiyordu — ikisi birbirinden bağımsız sürüklenebilir ve marker+kod hâlâ eşleşirken sayfa yanlış bilgi verebilirdi | **Düzeltildi.** `check-content.mjs`'e üçüncü bir kural eklendi: marker'ın hemen önündeki backtick'li literal, marker'ın kendi değeriyle eşleşmeli. 6 marker zaten uyumluydu (elle yazılan cümle bir kod bloğu/tablo hücresi içindeydi); 6 tanesine (`capabilities.md` ×3, `configuration.md` ×3'ün McpServer/Endpoint/Images bölümleri) küçük bir görünür literal eklendi — örn. "Off by default" → "Off by default (`false`)". Kasıtlı bozma ile doğrulandı: yalnız görünür `true`'yu `false` yapıp marker'a dokunmadan `npm run check:content` çalıştırıldı, kapı kırmızı oldu ve iki değeri de yazdı |
| 2 | 🟢 | `scripts/dokuman-bakim.py`'nin bu fazda dokunulmaması planın "gerekiyorsa" ifadesiyle tutarlı, ama bir gözlem | Devredildi — kayıt, kusur değil; bkz. Plandan Sapmalar |

Bulgular kapandıktan sonra dört kapı **yeniden** koşuldu (build, tam test
takımı, `dotnet pack`, `dotnet format`) ve site kapısı (`npm run check`);
hepsi yeşil.

## Sonraki Faza Devir Notu

- **Kapı şu an sekiz Options tipini tanıyor** (`DocumentedPolicyTests.
  ClaimableOptionTypes`). Yeni bir işaretli iddia eklerken tip listede yoksa
  kapı `check-content.mjs` içinde kırmızı olmaz ama `DocumentedPolicyTests`
  içinde açık bir hata mesajıyla kırmızı olur: "'<Tip>' is not registered in
  ClaimableOptionTypes". Kayıt kasıtlı — bkz. dosyanın kendi XML dokümanı.
- **Kapsam genişletme yolu:** yeni bir sayılabilir olmayan davranış iddiası
  işaretlenecekse aynı iki adım — (1) sayfada `<!-- claim:option
  Tip.Ozellik=deger -->` marker'ını görünür değerin (backtick literal)
  HEMEN önüne koy, (2) tip `ClaimableOptionTypes`'ta yoksa oraya ekle. Policy
  tarafı için marker `<!-- claim:policy METOD /yol scope=Kapsam -->` ve aynı
  görünür-literal kuralı geçerli.
  `Every_marked_endpoint_policy_claim_is_actually_enforced` HER marker için
  otomatik olarak yeni bir test host başlatıp iki anahtar oluşturuyor —
  yeni bir policy marker eklemek test kodunu değiştirmeden çalışır.
  `Every_marked_option_default_matches_the_real_type` de aynı şekilde marker
  listesini kendisi tarıyor.
  Faz 159 bu mekanizmaya dokunmuyor; F-171'in kalan kısmı (147 satırın
  geri kalanını kapsama genişletmek) `docs/ADAYLAR.md`'de aday olarak durur.
- **`TraconSqliteOptions.EnableReadViews`, `TraconSqlServerOptions.
  EnableReadViews` ve `TraconPostgreSqlOptions.EnableReadViews` aynı
  cümleyle (`read-views.md:38`) belgeleniyor** ama yalnız Sqlite işaretlendi
  (plan böyle diyordu). PostgreSQL/SqlServer eşdeğerleri henüz kapı altında
  değil — genişleme adayı.
