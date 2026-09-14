# Faz 170 — Production Profil Kapısı

> **Durum:** ✅ Tamamlandı (2026-09-14)
> **Plan onayı:** onaylandı 2026-09-14 (kullanıcı: "sıradaki fazı geliştir")
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-231**
> **Önkoşul:** [Faz 150](150-ZORUNLU-BINDING-PROFILI.md) — `RequireCustomBinding` + `RequiredBindingValidator` deseni; bu faz onun **options yarısıdır**
> **Paketler:** `Tracon.Abstractions` (risk enum'u + katkı sözleşmesi) · `Tracon.Core` (doğrulayıcı + beş yerleşik kontrol) · `Tracon.AspNetCore` (kiracı kontrolünün katkısı)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — bir `ITraconBuilder` üyesi, bir seçenek sınıfı, bir `enum`, bir istisna tipi, bir katkı arayüzü. `wc -l src/*/PublicAPI.Shipped.txt` → **17 satır / 17 dosya** (2026-09-14, yalnız `#nullable enable` başlıkları): **shipped giriş sıfır, bugün eklemek bedava.** GA'da `Shipped` dolduktan sonra aynı ekleme bir sürüm kararıdır
> **Tüketici yüzeyi:** Site: `guides/production.md` (Production-sensitive defaults tablosu ve dağıtım checklist'i — ikisi de 2026-09-14'te güncellendi, bu faz onlara kapıyı bağlar) · `guides/embedding.md` ("Make a binding required" bölümünün kardeşi) · `getting-started/security.md`
> · Sevk edilen: `ITraconBuilder`'ın XML `<example>`'ı · `src/Tracon.Core/README.md` · `capabilities.md` satırı
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md) — `MT-SEC-182`'den devam

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 27038de7:docs/arsiv/fazlar/170-PRODUCTION-PROFIL-KAPISI.md
> ```
>
> Damıtıldı 2026-09-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`AddTracon()` her genişleme noktasını `TryAdd` ile kaydeder ve güvenlik duyarlı her anahtar **izin verici** varsayılanla gelir. Bu K1'in ("sıfır sürpriz") doğru sonucudur: ilk koşumda hiçbir şey sürpriz yapmaz.

## Bitiş Ölçütleri (DoD)

> 🚨 **İki satır kapanışta yeniden yazıldı** (denetim 🔴 #2 · 🟡 #2). Gerekçeler
> **Plandan Sapmalar** §1, §2 ve §7'dedir; eski metin git geçmişindedir. Kural:
> doküman ile kod çelişirse **doküman yanlıştır**.

- [x] `RequireProductionProfile` **çağrılmayan** kurulumda hiçbir davranış değişmez — kompozisyon sırası ve servis çözüm anı dâhil (Faz 150 ile aynı şart). *(`Declaring_nothing_resolves_nothing` — atan bir `IServiceScopeFactory` ile ölçüldü · `A_host_that_does_not_declare_the_profile_starts_unchanged` · `ServiceRegistrationSnapshotTests`)*
- [x] Altı kalemin **her biri** için ayrı kanıt: kalem izin verici varsayılandayken host **başlamaz**. Tek bir toplu test yeterli sayılmaz. *(`One_permissive_decision_stops_the_host_and_is_named`, altı `InlineData`; her koşum diğer beşi karşılar ve yalnız birini açık bırakır)*
- [x] İçerik denetimi kalemi **kayıt yokluğuyla** ölçülür (`IEnumerable<IContentGuard>` boş), bir seçenek bayrağıyla değil — **ve kayıt tek başına yeterli sayılmaz**: guard kayıtlı olduğu hâlde `InspectInput` ve `InspectOutput` ikisi de kapalıysa kalem yine açıktır (denetim 🔴 #1). *(`Content_inspection_is_measured_by_the_registration_not_by_a_flag` · `A_registered_guard_that_inspects_nothing_is_still_uninspected_content` · `A_guard_that_inspects_one_direction_answers_the_inspection_decision`)*
- [x] **YENİDEN YAZILDI (Sapma §1).** Kiracı kalemi **iki** kontrolle yanıtlanır: `Tracon.Core` hangi `ITenantContext` bağlandığını okur, `Tracon.AspNetCore` `UseTenancy` içinden `Enabled`'ı ekler ve **en katı** cevap kazanır. `Tracon.Core` `TraconTenancyOptions`'a **referans vermez** ve `DependencyDirectionTests` temizdir. *Eski metin ("kalem yalnız AspNetCore'dan katkı olarak gelir") ölçümle düştü: AspNetCore'un her host'ta koşan bir kayıt noktası yoktur.* *(`UseTenancy_with_resolution_off_is_still_reported_as_single_tenant` · `UseTenancy_with_resolution_on_answers_the_tenant_decision` · `An_applications_own_tenant_context_answers_the_tenant_decision`)*
- [x] **YENİDEN YAZILDI (Sapma §2).** `MapTracon` çağırmayan gömülü host'ta kapı **koşar** ve kiracı kalemi `SingleTenantContext` adıyla **anlamlı** yanıtlanır — tek kiracılıysa `Permissive`, kendi `ITenantContext`'i bağlıysa `Satisfied`. *Eski metin (`NotApplicable` döner, host durmaz) Sapma §1'den sonra yanlıştı: gömülü bir host da çok kiracılı olabilir.* `NotApplicable` artık **kontrolsüz risk** için üretilir ve raporda sayılır (K-770). *(`The_gate_runs_in_a_host_with_no_HTTP_surface` · `A_risk_no_check_covers_is_reported_as_not_applicable_and_does_not_stop_the_host`)*
- [x] Kabul edilen risk host'u durdurmaz ve `Information` seviyesinde **adıyla** loglanır; kabul **yalnız** adlandırılan riski kapsar — komşu kalem yine durdurur. *(`An_accepted_risk_starts_the_host_and_is_logged_by_name` · `An_accept_does_not_cover_the_decision_next_to_it`; iddia **tek bir log girdisinin** iki yarıyı birden taşıdığını ölçer — K-642 bölünmüş ifade tuzağı)*
- [x] **YENİDEN YAZILDI (Sapma §7).** Eski satır (`AddTracon`'dan **önce** çağrılan kurulum başlamaz) **yapısal olarak konusuzdur**: `RequireProductionProfile` bir `ITraconBuilder` üyesidir ve builder yalnız `AddTracon()` döndükten sonra vardır. Gerçek sıra sorusu tüketicinin **kendi kontrolünün** kaydıdır ve `AddTracon`'dan önce kaydedilen bir kontrol listeye katılır. *(`An_applications_own_check_joins_the_decision_and_the_strictest_answer_wins`, `configureServices` `AddTracon`'dan **önce** koşar)*
- [x] Hata mesajı üç bilgiyi taşır: hangi kalem · bugünkü değer · nasıl düzeltilir. Açık kalemlerin **hepsi** tek mesajda listelenir. *(`The_message_carries_the_item_todays_value_and_the_fix` · `Every_open_item_is_named_in_one_message`; sözleşme **yapısaldır** — `ProductionProfileResult.Permissive(...)` üçünü de zorunlu alır)*
- [x] Hata mesajı, istisna ve log hiçbir seçenek **değeri** veya `secret` taşımaz; canary değerli bir kurulumla ölçülmüştür (K-059). *(`Neither_the_failure_nor_the_log_carries_a_configured_value` — dolu bir `ContentProtection:Keys` haritasıyla · `Neither_the_message_nor_the_log_carries_a_configuration_value` · `kapi.py tarama` temiz)*
- [x] Bir kontrol istisna atarsa host başlamaz ve hangi kontrolün attığı yazılır. *(`A_check_that_throws_stops_the_host_and_names_the_check`)*
- [x] AOT smoke koşar; kontrol kaydı açıktır, hiçbir yerde tip taraması yoktur. `Abstractions`/`Core` AOT uyumluluğu korunur. *(`kapi.py kapanis` içindeki AOT smoke; `Enum.GetValues<T>()`/`Enum.IsDefined<T>()` generic aşırı yüklemeleri ve `GetServices<T>()` kapalı generic'tir — `GetServices(Type)` kullanılmadı)*
- [x] `reference/compatibility.md` yükseltme sözleşmesini taşır: profile anahtar eklemek **davranışsal kırıcı değişikliktir** ve `CHANGELOG.md`'de öyle ilan edilir (§170.4). *(yeni bölüm: "The production profile is a versioned contract" · `CHANGELOG.md` Unreleased/Added · `ITraconBuilder` XML `<remarks>` — K-773)*
- [x] `guides/production.md`'nin "Production-sensitive defaults" tablosu ve dağıtım checklist'i kapıya bağlanır; `guides/embedding.md` "Make a binding required" bölümünün kardeşi yazılır. *(ayrıca `concepts/governance.md`, `getting-started/security.md`, `capabilities.md`, `packages.md` — site senkron kapısı dört kuralın dördünü de yeşil verdi)*
- [x] Dört doğrulama kapısı sıfır uyarı verir: `python3 scripts/kapi.py kapanis --taban 7079e3a9`.
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — hem profil **çağrılmadan** hem çağrılıp karşılanarak. *(bkz. "Örnek Uygulama Koşumu"; geçici yama geri alındı, `git diff --stat samples/` boş)*
- [x] `secret` taraması boş döndü.
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine `MT-SEC-182`'den eklendi ve `00-INDEKS.md` sayacı güncellendi (132 → 140); otomatikleştirilebilenlerin **hepsi** koşuldu.
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı.

### Doğrulama komutları

```bash
# Profil çağrılmayan host aynı kalır
dotnet run --project samples/Tracon.Api

# Altı kalemin ayrı kanıtı
./artifacts/bin/Tracon.AspNetCore.FunctionalTests/release/Tracon.AspNetCore.FunctionalTests \
  --filter-class "*ProductionProfileStartupTests*"

# Mesaj ve log secret taşımıyor
python3 scripts/kapi.py tarama
```

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez** — sonraki oturumun en değerli
> bilgisidir.

### 1 — 🚨 Planın §170.2 YAPISAL iddiası DÜŞTÜ: `Tracon.AspNetCore`'un her host'ta koşan bir kayıt noktası YOK

Plan şunu söylüyordu: *"`Tracon.AspNetCore` kiracı kontrolünü `TryAddEnumerable`
ile **ekler**."* `faz-uygulama` Adım 1 bunu ölçtü ve iddia düştü.

```bash
grep -rn "public static ITraconBuilder AddTracon" src/   # yalnız Tracon.Core
grep -rn "this IServiceCollection\|this ITraconBuilder" src/Tracon.AspNetCore/
grep -rn "TryAddEnumerable\|IHostedService\|IStartupFilter" src/Tracon.AspNetCore/
```

Ölçüm: `AddTracon` **tamamen `Tracon.Core`'dadır**. `Tracon.AspNetCore` üç
opt-in zincir uzantısı sunar (`UseTenancy` · `UseMcpServer` · `UseA2A`) ve bir
de `MapTracon` — ki o, kap **kurulduktan sonra** koşar ve servis kaydedemez.
Paketin her host'ta koşan bir kayıt noktası **yoktur**.

Sonucu şudur: kontrolü `UseTenancy` içine koymak kapıyı **delik** bırakır.
`UseTenancy` hiç çağırmayan bir host — yani kapının yakalaması gereken tam
durum — kiracıyı hiç sormaz.

👤 **Kullanıcı kararı (2026-09-14).** Kiracı sorusu **iki** kontrolle yanıtlanır:

| Katman | Neye bakar | Cevap |
|---|---|---|
| `Tracon.Core` · `TenancyProfileCheck` | Kaba **hangi `ITenantContext`** bağlandı | Yerleşik `SingleTenantContext` ise `Permissive`, değilse `Satisfied` |
| `Tracon.AspNetCore` · `TenancyResolutionProfileCheck` | `TraconTenancyOptions.Enabled` | `UseTenancy` çağrıldıysa kayıtlıdır; `false` ise `Permissive` |

Doğrulayıcı risk başına **en katı** cevabı alır (`Permissive` > `Satisfied` >
`NotApplicable`). Bu, planın kapatamadığı deliği de kapatır:
`UseTenancy(options => options.Enabled = false)` `ITenantContext`'i
**değiştirir**, yani Core'un cevabı tek başına `Satisfied` olurdu — oysa her
istek hâlâ varsayılan kiracıya çözülür.

Kazanç plandan büyüktür: kiracı sorusu artık **gömülü host'ta da** anlamlı
yanıtlanır ve kendi `ITenantContext`'ini bağlayan (kiracıyı bir kuyruk
başlığından çözen) bir tüketici, HTTP kiracılığı hiç kullanmadan `Satisfied`
sayılır.

### 2 — `NotApplicable` gömülü host'ta kiracı için DEĞİL, kapsanmayan risk için üretilir

Planın DoD'si ve Manuel Case 6, `MapTracon` çağırmayan gömülü host'ta kiracı
kaleminin `NotApplicable` dönmesini istiyordu. Sapma 1'den sonra bu **yanlış**
olurdu: gömülü bir host da pekâlâ çok kiracılı olabilir ve Core'un kontrolü ona
anlamlı bir cevap verir.

👤 **Kullanıcı kararı (2026-09-14):** üç değerli model korunur (§170.3'ün
kullanıcı kararı), ama üçüncü değer artık şu iki durumda üretilir:

1. Doğrulayıcı **altı riskin hepsini** dolaşır; bir riski taşıyan **hiç kontrol
   kayıtlı değilse** o kalem `NotApplicable` olarak loglanır ve raporda sayılır.
   Bugün altısının da Core'da kontrolü vardır, yani bu dal gelecek içindir: bir
   paketin kendi riskini katması (ör. yalnız `Tracon.PostgreSql` ile anlamlı bir
   karar) o paketi kurmayan host'u durdurmaz, **gizlemez de**.
2. Bir kontrol **kendisi** `ProductionProfileResult.NotApplicable(...)` döner.
   Bu, `IProductionProfileCheck` public sözleşmesinin bir parçasıdır; Tracon'in
   sevk ettiği altı kontrolün hiçbiri bugün bunu dönmez, çünkü altısının da
   sorusu her kompozisyonda anlamlıdır.

Manuel Case 6 bu gerçeğe göre `MT-SEC-189` olarak yeniden yazıldı: gömülü
host'ta kapı **koşar** ve kiracı kalemi `SingleTenantContext` adıyla raporlanır.

### 3 — `TraconProductionProfileException` EKLENMEDİ (K-699'un tekrarı)

Plan bir istisna tipi listeliyordu. Faz 150 aynı kararı zaten vermişti (K-699):
ihlal `InvalidOperationException` atar, `TraconException` ailesine yeni tip
eklenmez. Gerekçe birebir geçerlidir — tüketicinin yakalayacağı bir şey yoktur
(host zaten başlamaz) ve yeni bir istisna tipi public yüzeyi bedelsiz büyütür.
Plan bu emsali görmemişti; uygulama onu izledi. Public yüzey planlanandan **bir
tip küçük** çıktı.

### 4 — `ProductionProfileResult` bir `record` DEĞİL, üç fabrikalı bir sınıf

Plan `public sealed record ProductionProfileResult(ProductionProfileState State,
string Detail)` öngörüyordu. İki sebeple değişti:

- **`Detail` tek dize, DoD ise üç bilgi istiyor** (kalem · bugünkü değer · nasıl
  düzeltilir). Üçü ayrı alan olunca sözleşme **yapısal** olur: `Permissive(...)`
  fabrikası üçünü de zorunlu alır, yani eksik bir mesaj **yazılamaz**. Tek dize
  bunu bir yazım geleneğine bırakırdı.
- **`record`'un üretilen `ToString`'i her alanı basar.** Bu tip bir tüketici
  kontrolünün döndürdüğü metni taşır ve doğrudan başlangıç istisnasına girer;
  nesneyi biçimlendiren herhangi bir log satırı ikinci, denetlenmemiş bir
  ifşa yolu olurdu. Aynı gerekçe `TraconTenancyOptions`'ın kendi XML'inde de
  yazılıdır.

### 5 — Altı kontrol `AddTracon()` içinde kayıtlıdır, `RequireProductionProfile()` içinde değil

Plan kayıt yerini söylemiyordu. Kontroller `AddTracon()` içinde açıkça
(`TryAddEnumerable`) kaydedilir; `RequireProductionProfile()` yalnız
**beyanı** kaydeder. Sebep: `UseTenancy`'nin katkısı zincirde
`RequireProductionProfile`'dan **önce** de sonra da gelebilir ve kaydı tek bir
çağrıya bağlamak o sırayı anlamlı kılardı. Bedeli altı `ServiceDescriptor`'dur
ve doğrulayıcı beyan yoksa **hiçbirini çözmez** — `ProductionProfileValidator`
`IEnumerable<IProductionProfileCheck>`'i kurucusunda **almaz**, scope'tan
çözer.

### 6 — `samples/` değiştirilmedi

Faz 150 `samples/Tracon.Embedded`'a dört zorunlu binding eklemişti. Buraya
eşdeğeri eklenmedi: profil altı kalemin **hepsini** ister ve örnek uygulamayı
o hâle getirmek, aynı örneğe dayanan diğer manuel case ailelerinin zeminini
değiştirirdi. DoD kanıtı bunun yerine **geçici** bir yamayla ölçüldü (aşağıda),
yama geri alındı ve `git status` temiz doğrulandı.

### 7 — "`AddTracon`'dan önce çağrılan kurulum" hata modu YAPISAL OLARAK KONUSUZ

Planın hata modu tablosu ve bir DoD satırı, Faz 150'nin `TryAdd` tuzağını buraya
taşıyordu: *"`AddTracon`'dan **önce** çağrılınca kayıt `TryAdd` yüzünden
kaybolur."* Bu buraya uymuyor:

- `RequireProductionProfile` bir **`ITraconBuilder` üyesidir** ve builder yalnız
  `AddTracon()` **döndükten sonra** vardır — "önce çağırmak" diye bir çağrı yeri
  yoktur.
- Beyan `AddSingleton(new ProductionProfileRegistration(...))` ile kaydedilir,
  `TryAdd*` ile değil; yani düşecek bir kayıt da yoktur (aynı sebep K-667'de
  yazılıdır ve idempotence okuyan tarafta sağlanır).

Sıra sorusunun **gerçek** karşılığı tüketicinin **kendi kontrolüdür**:
`IProductionProfileCheck`'i `AddTracon`'dan önce kaydeden bir modülün kontrolü
listeye katılır ve en katı cevap yine kazanır.
`An_applications_own_check_joins_the_decision_and_the_strictest_answer_wins`
bunu ölçer — `configureServices`, `AddTracon`'dan **önce** koşar.

### 8 — 🚨 İçerik denetimi kalemi: KAYIT gerekli ama YETERLİ değil (denetim 🔴 #1)

Plan ve ilk uygulama kalemi tek bir soruyla ölçüyordu: `IEnumerable<IContentGuard>`
boş mu? Bağımsız denetim bunun **yetmediğini** buldu ve repro'yu kodda gösterdi:

```
TraconContentGuardOptions.InspectInput  = true (varsayılan)  -> false yapılabilir
TraconContentGuardOptions.InspectOutput = true (varsayılan)  -> false yapılabilir
ContentGuardingChatClient.cs:52,58 — ikisi de HER çağrıda okunur
```

Guard kayıtlıyken iki bayrağı da kapatan bir kurulumda sarmalayıcı boru hattına
**eklenir**, her çağrıda koşar ve **hiçbir şeye bakmaz** — yani
`TraconProductionRisk.UninspectedContent`'in kendi XML'inin tarif ettiği durumun
tam kendisi. Kapı ise `Satisfied` diyordu.

Düzeltme, önce **düşen bir test** yazılarak yapıldı
(`A_registered_guard_that_inspects_nothing_is_still_uninspected_content` —
düzeltmeden önce kırmızı koştuğu doğrulandı):

| Durum | Cevap |
|---|---|
| Guard kayıtlı değil | `Permissive` — "no content guard is registered" |
| Kayıtlı, iki bayrak da kapalı | `Permissive` — "neither input nor output is inspected" |
| Kayıtlı, en az biri açık | `Satisfied` |

**Kısmi inceleme bilerek `Satisfied` sayılır.** Tek bir yönü kapatmak, tüketicinin
**yazarak** verdiği açık bir karardır; riskin kendi tanımı ise "hiçbir istem veya
yanıt incelenmez" der. Kapı kararın alınıp alınmadığını sorar, kararın doğru
olup olmadığını değil.

### 9 — Kiracı incelmesinin `Setting`'i bir yapılandırma anahtarı DEĞİL, çağrının kendisidir (denetim 🟡 #1)

İlk uygulama `TenancyResolutionProfileCheck`'in `Setting` alanına
`"Tracon:Tenancy:Enabled"` yazıyordu. Denetim ölçtü: o anahtarı **kütüphane hiç
bağlamaz** — `UseTenancy` yalnız `AddOptions<TraconTenancyOptions>()` +
`Configure(configure)` yapar ve `Tracon:Tenancy` bölümü hiçbir yerde `Bind`
edilmez; anahtar yalnız `samples/Tracon.Api`'nin **kendi** kodunda okunur
(`grep -rn "Tracon:Tenancy" src/` → yalnız o kontrolün kendisi).

Sonuç gerçek bir kusurdu: tüketici mesajı okur, `appsettings.json`'a o anahtarı
yazar ve host **aynı mesajla yine durur**. `Setting` artık gerçek yüzeyi
gösterir: `UseTenancy(options => options.Enabled)`. Diğer beş kontrolün
`Setting`'i `SectionName` sabitinden türer ve o anahtarlar gerçekten bağlanır;
bu kontrolün ayrık olmasının sebebi budur ve koda yorum olarak yazıldı.

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-769** — Bir üretim kararı **iki** kontrol taşıyabilir ve risk başına **en katı** cevap kazanır *(kullanıcı kararı)* | `Tracon.AspNetCore`'un her host'ta koşan bir kayıt noktası yoktur (Sapma 1), yani kiracı sorusu tek katmandan doğru yanıtlanamaz. Core "hangi `ITenantContext` bağlandı" sorusunu her kompozisyonda (gömülü host dâhil) yanıtlar; `UseTenancy` ise yalnız kendi görebildiği `Enabled=false` durumunu ekler. Alternatif — kontrolü yalnız `UseTenancy`'ye koymak — kapının yakalaması gereken tam durumu (hiç `UseTenancy` çağırmayan host) kör bırakırdı. |
| **K-770** — `NotApplicable`, bir riski taşıyan **hiç kontrol kayıtlı olmadığında** üretilir; sevk edilen altı kontrolün hiçbiri bunu dönmez *(kullanıcı kararı)* | Planın örneği (gömülü host + kiracı) Sapma 1'den sonra yanlıştı: o kalemin gerçek ve anlamlı bir cevabı vardır. Üçüncü değer yine de kalır, çünkü `IProductionProfileCheck` public bir seam'dir ve bir paketin kendi riskini katması hâlinde o paketi kurmayan host'un kalemi **gizlenmemeli**, sayılmalıdır. |
| **K-771** — Toplu kabul yolu (`AcceptAll()`) yoktur ve eklenmeyecektir | Kabul **kalem başına** yazılır; altı satırlık bir kabul listesi kod incelemesinde görünür, tek satırlık bir `AcceptAll()` görünmez ve kapıyı tam olarak kaldırmak istediği sessizliğe geri çevirir. Aynı sebeple her kabul `Information` seviyesinde adıyla loglanır. |
| **K-772** — `ProductionProfileResult` bir `record` değildir ve üç fabrikayla kurulur | `record`'un üretilen `ToString`'i, tüketici kontrolünün yazdığı metni herhangi bir log satırına ikinci bir ifşa yolu olarak taşır. Fabrikalar ayrıca DoD'nin "üç bilgi" sözleşmesini **derleme anında** zorlar: `Permissive` çağrısı bugünkü değer ve düzeltme olmadan yazılamaz. |
| **K-773** — Profil kümesi bir **sürüm sözleşmesidir**; kümeye anahtar eklemek davranışsal kırıcı değişikliktir | §170.4'ün yayın yükümlülüğü koda ve üç sevk edilen metne bağlandı: `ITraconBuilder` XML `<remarks>`, `reference/compatibility.md`'nin kendi bölümü ve `CHANGELOG.md` girdisi. Yeni bir kararın sessizce atlanması tam olarak engellenmek istenen şeydir; bedeli önceden yazılıdır. |

Faz 150'nin **K-699**'u (yeni istisna tipi eklenmez) burada **yeniden
uygulandı**, yeniden açılmadı — bkz. Sapma 3.

## Örnek Uygulama Koşumu (DoD kanıtı)

`samples/Tracon.Api`, `Release`, gerçek süreç. Üç koşum:

| # | Kurulum | Ölçülen |
|---|---|---|
| 1 | Değiştirilmemiş (profil **çağrılmamış**) | `Application started` **1** · `Production profile` geçen satır **0** |
| 2 | Geçici `tracon.RequireProductionProfile();` | Süreç `exit=134` · `Application started` **0** · mesaj **5** kalemi ayrı ayrı saydı |
| 3 | `Accept(SingleTenant)` + `Accept(UnencryptedContentAtRest)`, diğer üçü ortam değişkeniyle açık | `Application started` **1** · `GET /api/meta` → `200` · log'da **iki** `Information` satırı, riskleri adıyla |

Koşum 2'nin altı kalemden **beşini** sayması bir kusur değil, kanıttır: örnek
uygulama zaten bir içerik guard'ı kaydeder, dolayısıyla `UninspectedContent`
listede **yoktur** — kalem bir bayrakla değil, **kaydın varlığıyla** ölçülüyor.

Koşum 2'nin tam mesajından bir kalem:

```text
RequireProductionProfile() was called, and 5 production decisions are still on the
permissive default, so the host does not start.

  SingleTenant
    Setting: ITenantContext
    Today:   resolves to Tracon's built-in SingleTenantContext, so every call runs as the default tenant
    Fix:     register your own ITenantContext before the AddTracon() call, or on an ASP.NET Core host call UseTenancy(options => options.Enabled = true)
```

Koşum 3'ün kabul satırları:

```text
info: Production profile: SingleTenant is accepted. ITenantContext is permissive: resolves to
      Tracon's built-in SingleTenantContext, so every call runs as the default tenant.
info: Production profile: UnencryptedContentAtRest is accepted. Tracon:ContentProtection:Enabled
      is permissive: off, so prompts, responses and tool arguments are stored as clear text.
```

Gerçek `run`: aynı kapılı host üzerinde
`POST /tracon/api/agents/cached-support/run` SSE akışı `run` → `update` ×3 →
`done` turunu verdi; `GET /tracon/api/stats` `totalRuns:1`,
`completedRuns:1` döndü. Geçici yama geri alındı;
`git diff --stat samples/` boş.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan revize edilmedi; iki yapısal iddiası uygulama sırasında ölçülüp **Plandan Sapmalar** bölümüne yazıldı (Sapma 1 · 2), ikisi de kullanıcıya sorularak karara bağlandı |
| Düzeltme turu sayısı | 2 — (1) `RS0016` public API girdileri + `CA1859`, (2) dört ratchet kapısı (`CapabilityCoverage` · `SeamContract` · `PublicSurface` · `ServiceRegistrationSnapshot`) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **2 / 0 / 0** — ikisi de kullanıcı triyajında **gerçek** sayıldı ve kapatıldı (biri kod + düşen test, biri doküman) |
| Fazın ürettiği regresyon | 0 — 2667 Core birim testi ve etkilenen fonksiyonel setler yeşil; `AddTracon` kayıt sırası snapshot'ı yalnız **eklenen** yedi satırla değişti |
| Faz kapandıktan sonra bulunan kusur | — (kapanış anında boş; sonraki oturum doldurur) |

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi (taze bağlam, yalnız DoD + diff, salt-okunur)
**iki 🔴**, **dört 🟡** ve **üç 🟢** bulgu üretti. 🔴'ların triyajını kullanıcı
yaptı (K-768); ikisi de **gerçek** sayıldı. Hepsi kapandı ya da gerekçelendi.

| # | Seviye | Bulgu | Triyaj | Kapanış |
|---|---|---|---|---|
| 1 | 🔴 | İçerik denetimi kalemi yalnız KAYDA bakıyor; guard kayıtlıyken `InspectInput`/`InspectOutput` ikisi de kapatılabilir ve kapı yine `Satisfied` der | **gerçek** 👤 | Düzeltildi — Sapma §8. Önce düşen test yazıldı (`A_registered_guard_that_inspects_nothing_is_still_uninspected_content`, düzeltmeden önce kırmızı koştuğu doğrulandı), sonra kontrol iki bayrağı da okur hâle geldi. Kısmi incelemenin `Satisfied` kaldığı ayrıca bir testle kilitlendi |
| 2 | 🔴 | DoD listesi sevk edilen davranışın **tersini** söylüyor (gömülü host'ta kiracı kalemi `NotApplicable`); Sapma §2 doğruyu anlatıyor ama DoD güncellenmemiş | **gerçek** 👤 | Düzeltildi — DoD'nin iki satırı yeniden yazıldı, ikisi de sapma numarasına bağlandı ve her satıra kanıtı (test adı) eklendi. Denetçinin uyarısı kayda geçti: eski DoD'yi okuyan sonraki oturum `TenancyProfileCheck`'i `NotApplicable`'a çevirir ve K-769'un kapattığı deliği geri açardı |
| 3 | 🟡 | `TenancyResolutionProfileCheck`, kütüphanenin hiç bağlamadığı bir yapılandırma anahtarını (`Tracon:Tenancy:Enabled`) `Setting` olarak yazıyor | — | Düzeltildi — Sapma §9. `Setting` artık `UseTenancy(options => options.Enabled)`; fonksiyonel test ve `MT-SEC-187` birlikte güncellendi |
| 4 | 🟡 | "`AddTracon`'dan önce çağrılınca" hata modu ve DoD satırı için ne test var ne yazılı gerekçe | — | Gerekçelendi — Sapma §7: satır yapısal olarak konusuzdur (`RequireProductionProfile` bir `ITraconBuilder` üyesidir). Gerçek sıra sorusu tüketicinin kendi kontrolüdür ve zaten test edilmiş |
| 5 | 🟡 | Kabul log'u iddiası **bölünmüş ifade** ile ölçülüyor (K-642 tuzağı): `ShouldContain("SingleTenant")` + ayrı `ShouldContain("accepted")`, ve `SingleTenantContext` zaten `SingleTenant` içeriyor | — | Düzeltildi — iki fonksiyonel iddia **tek bir log girdisinin** `"<Risk> is accepted"` taşıdığını ölçer hâle geldi |
| 6 | 🟡 | Sapma §1'in tuzağı alan hafızasına girmedi; yalnız faz dokümanında duruyor ve o kapanışta arşive gidiyor | — | Düzeltildi — not [`docs/hafiza/aspnetcore-di.md`](../../hafiza/aspnetcore-di.md) başına eklendi (ölçüm komutlarıyla birlikte) |
| 7 | 🟢 | `AcceptedRisks` arkadaki `HashSet`'i doğrudan döner; `IReadOnlyCollection`'dan downcast ile boşaltılabilir | — | Alınmadı. Tüketicinin **kendi** options nesnesidir ve hiçbir güvenlik sınırı geçmez; kendi kabul listesini boşaltan bir tüketici yalnız kapıyı sıkılaştırmış olur |
| 8 | 🟢 | Kayıt yolu mesajı `Tracon:ContentGuard:Pattern` bölümünü bir yol olarak saymıyordu | — | 🔴 #1 düzeltilirken kapandı: mesaj artık üç yolu da sayar |
| 9 | 🟢 | `Severity`'nin `_ => 0` dalı, gelecekte eklenecek bir `ProductionProfileState` değerini en gevşek sayar | — | Alınmadı. Fabrikalar `private` kurucuyu sarmaladığı için bugün ulaşılamaz; `ProductionProfileState`'e değer eklemek zaten K-773'ün ilan yükümlülüğüne tabidir |

**Denetçinin temiz bulduğu başlıklar:** 3.3 (test seviyesi) · 3.5 (imza-gövde
kayması) · 3.6 (plan dışı public API) · 3.7 (repo kuralları) · 3.8 (ürün yüzeyi).

**Denetçinin ölçmediğini yazdığı yer:** salt-okunur olduğu için dört kapıyı,
`dotnet build`'i ve AOT smoke'u koşmadı. Bunlar uygulayan oturum tarafından
koşuldu (`kapi.py kapanis --taban 7079e3a9`, çıkış 0) ve 🔴 düzeltmelerinden
**sonra** tekrar koşuldu.

## Sonraki Faza Devir Notu

- 🚨 **`Tracon.AspNetCore`'un HER host'ta koşan bir servis kayıt noktası
  YOKTUR.** `AddTracon` tamamen `Tracon.Core`'dadır; AspNetCore yalnız opt-in
  zincir uzantıları (`UseTenancy` · `UseMcpServer` · `UseA2A`) ve `MapTracon`
  sunar — ve `MapTracon` kap kurulduktan **sonra** koşar, servis kaydedemez.
  "AspNetCore'dan katkı gelsin" diyen bir plan cümlesi yazmadan önce o katkının
  **hangi çağrıda** kaydedileceğini söyle; opt-in bir çağrıya bağlanan katkı,
  o çağrıyı yapmayan host'u kör bırakır.
- **Bir üretim kararına ikinci bir kontrol eklemek serbesttir** (K-769):
  `TryAddEnumerable(ServiceDescriptor.Singleton<IProductionProfileCheck, X>())`
  yeter, risk başına **en katı** cevap kazanır. Bir kontrolü "sadeleştirip"
  tekilleştiren bir değişiklik, `UseTenancy(Enabled=false)` deliğini geri açar.
- **Profile yeni bir anahtar eklemek DAVRANIŞSAL KIRICI DEĞİŞİKLİKTİR**
  (K-773). Üç yerde birden ilan edilir: `CHANGELOG.md` (neden eklendiğiyle),
  `reference/compatibility.md`'nin kendi bölümü ve `ITraconBuilder` XML
  `<remarks>`'ı. `TraconProductionRisk`'e değer eklerken üçünü de güncelle;
  yalnız enum'a satır eklemek sözleşmeyi sessizce kırar.
- **Toplu kabul yolu eklenmeyecektir** (K-771). Bir sonraki faz
  "tüketici altı satır yazıyor" diye `AcceptAll()` önerirse, karar defterindeki
  gerekçe bunu zaten yanıtlar.
- **Kapı `IHost` gerektirir** — Faz 150'nin devir notuyla aynı sınır.
  `IHostedService`'tir; `AddTracon()` + `BuildServiceProvider()` ile duran bir
  giriş noktası (bugün yalnız `Tracon.Cli/Commands/SqlProviderSelector.cs`)
  kapıdan geçmez.
- **Kapı bir kompozisyon kapısıdır, güvenlik kanıtı değildir.** XML, site
  metni, `capabilities.md` ve `CHANGELOG` bunu dört kez yazıyor; "Tracon
  production'ı güvenli hâle getiriyor" diyen bir metin yazma. Kapı bir anahtarın
  **açık** olduğunu söyler, arkasındaki politikanın doğru olduğunu değil.
