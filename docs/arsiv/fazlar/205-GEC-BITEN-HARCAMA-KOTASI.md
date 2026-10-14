# Faz 205 — Kök Kapandıktan Sonra Biten Harcamanın Kotası

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** 2026-10-14 — kullanıcı "Faz 205'i geliştir" dedi; engelleyici dört tasarım sorusu aynı gün cevaplanmıştı (§205.1 A, F-482 dahil, webhook belgelenir, eşik bildirimi çocuğun akışı). Açık Soru 1-4 önerilen seçenekle (hepsi A) uygulandı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-418**, **F-482**
> **Genellik:** Alt agent çağıran ya da tool'u zaman aşımına düşebilen her tüketici, kiracı kotası açıksa etkilenir. İş tüketici kodunda kalamaz: kota sayacını yalnız Tracon yazar. Kalem bir sözleşme ihlalidir (kiracı kotası bir yönetişim sınırıdır, site "consumption is counted" der), bu yüzden § *Tüketici geri bildirimi kuralı*'ndan muaftır. İkinci kanıt ilk ilkedir: K-913 kendi bilinen sınırını yazar ve bir model talimatı ("görevi başlat, bekleme") harcamayı kotadan gizler
> **Karşı görüş:** "Arka plan alt run'ı `ChildDeadline` (2 dk) ile sınırlıdır; kaçak küçük ve nadirdir." Cevap: sınır run başınadır, toplam değildir. Bir kök en çok `MaxTotalRuns` (25) çocuk başlatır ve her biri 2 dakikaya kadar harcar; aynı istek tekrarlanınca kaçak sınırsız büyür. Geç biten tool'un harcaması (F-482) gerçek paradır ve bugün hiçbir sayaca girmez. Mekanizma ikisini de aynı kuralla kapatır
> **Önkoşul:** Yok — K-913 (Faz dışı, F-383) ve Faz 199 (K-972…K-974) kapalı
> **Paketler:** `Tracon.Abstractions` (yalnız `internal` üye), `Tracon.Core`, `Tracon.Workflows`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — taşıyıcı `internal`'dır (§205.2). Ölçüldü (2026-10-14): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır, her biri yalnız `#nullable enable`; bir sapma public üye gerektirirse GA'dan (K-603) önce hâlâ ucuzdur
> **Tüketici yüzeyi:** site: `concepts/governance.md` (kota bölümü: "counted once at the tree's root" cümlesi ve eşik bildirimi), `concepts/agents.md` (arka plan alt agent'ı, yalnız bir paragraf varsa), `guides/background-work.md`'ye dokunulmaz · sevk edilen: `CHANGELOG.md` (davranış değişikliği), `capabilities.md` kota satırı
> **Manuel test alanı:** `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` (kota case'leri, `MT-RET-*`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 4aacdc83:docs/arsiv/fazlar/205-GEC-BITEN-HARCAMA-KOTASI.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Kiracı kotası bir çağrı ağacının harcamasını eksiksiz saymalıdır. Bugün harcama yalnız kökün kapanış anında okunur: o anda hâlâ koşan arka plan alt run'ı ve zaman aşımından sonra biten tool'un tutarı hiçbir sayaca girmez.

## Bitiş Ölçütleri (DoD)

- [x] Bekleme yapmayan kökün çocuğu bitince kökün sayacı kök + çocuğu taşır; `runs` 1 (`BackgroundChildQuotaTests`, akışlı ve akışsız; taban sürümde kırmızı: sayaç 10, beklenen 20)
- [x] Workflow adımının arka plan görevi workflow bitince sayaca girer (`A_workflow_steps_background_task_is_counted_under_the_workflow`; taban sürümde kırmızı)
- [x] Geç biten tool'un tutarı sayaca girer, run snapshot'ı değişmez (`LateToolCompletionTests`; taban sürümde kırmızı)
- [x] `AgentTreeQuotaTests`, `NonTokenSpendAgentTreeTests`, `NonTokenSpendTests` değiştirilmeden yeşil
- [x] `QuotaStoreContract` `Runs = 0` case'i dört depoda yeşil (bellek içi · Sqlite · PostgreSQL · SQL Server; iki case)
- [x] K-913 ve K-974'ün kota yarısını değiştiren karar `KARARLAR.md`'ye yazıldı (K-990, kategori: güvenlik, public-api)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`kapi.py kapanis --taban 245afe91` çıkış 0: build · test 691 sn · pack · format; sözleşme paketine dokunulduğu için `kapi.py yayin --kuru` ayrıca koşar)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1 ve 3), çıktı aşağıda
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] Manuel kabul case'leri `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklendi (`MT-RET-081…084`); otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (1 gerçek, düzeltildi)
- [x] `docs-site/` güncellendi (`concepts/governance.md`, `runs.md`, `tools.md`, `workflows.md`, `capabilities.md`); `npm run check` temiz

### Doğrulama komutları

```bash
# Arka plan çocuğunun kotası
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*BackgroundChildQuotaTests" "*AgentTreeQuotaTests" "*LateToolCompletionTests"

# Örnek uygulamada sayaç (çocuk bitmeden ve bittikten sonra)
curl -s http://localhost:5081/tracon/api/quotas/usage -H "Authorization: Bearer $TOKEN"
```

---

## Plandan Sapmalar

1. **Taşıyıcı `TreeAccount` plandaki taslaktan farklıdır.** `RootRunId` düştü (hiçbir okuyucusu yok); `Quota` (`IQuotaRecorder?`) eklendi. Gerekçe: geç tool yolu yalnız yakaladığı `AgentRunScope`'u tutar ve `TimeoutAIFunction` public bir tiptir — `QuotaEnforcer`'ı ona vermek yerine tohum bütçedeki hesaba bindi. Taslaktaki `IQuotaEnforcer` parametresi `IQuotaRecorder` oldu (`QuotaEnforcer` onu uygular; `Tracon.Abstractions` içinde, `internal`). Kök hesabı **bir kez değil her seferinde yeniden** atar: bir bütçeyi ikinci kök için yeniden kullanan tüketicinin çocukları birincinin kiracısına yazmasın (`TreeQuotaTests.A_budget_a_second_root_reuses_names_the_second_root`).
2. **Para birimi kuralı plandaki gibi uygulanamadı.** Plan "`Tracon:Pricing:Currency` dışındaki tutar sayılmaz" diyordu; ama `AgentTreeQuotaTests.A_tree_priced_in_two_currencies…` yapılandırılmış birim `null` iken koşar ve çocuk kapanırken kökün birimi bilinmez (çocuk kökten önce kapanır). Uygulanan: **kök kendi maliyetini olduğu gibi yazar**; alt run'ın token maliyeti etiketsizse ya da yapılandırılmış birimdeyse girer, etiketli ve farklıysa token ve token dışı tutar girer (`RunRecordingAgent.QuotaAmount`, `TreeQuotaTests` teorileri). Bedel: özel çözümleyici bütün ağacı yapılandırılmışın dışında bir birimle fiyatlıyorsa alt run'ların token maliyeti artık eklenmez (**F-499**). Mevcut test değişmedi.
3. **Geç tool tutarı çağrının kaydından değil birikimciden okunur.** Plan "`ToolCallUsage.Cost` varsa girdi yazılır" diyordu; kayıttan okumak, tool'un süresi içinde raporlayıp run kapandıktan sonra dönmesi hâlinde tutarı iki kez sayardı (kayıt da toplam da tutarı tutar). `NonTokenCostAccumulator.TakeLateToolAmount` yalnız mühürden **sonra** raporlananı verir ve mühürdeki aynı çağrının tutarını düşer (son rapor öncekinin yerine geçer; denetimin 🔴 bulgusu). Plandaki yapısal iddia (geç ölçüm yalnız çağrı satırına yazılır) doğruydu; eksik olan "kapanıştan önce mi sonra mı" ayrımıydı.
4. **Public yüzey `Tracon.Testing.Contracts.Xunit`'te iki test yöntemi büyüdü.** Plan "Public API büyümüyor" diyordu ve `Tracon.Abstractions`/`Core`/`Workflows` için doğrudur; ama sözleşme paketinin kendisi izlenir (`PublicAPI.Unshipped.txt` +2 satır): `QuotaStoreContract.Usage_that_adds_no_run_adds_its_tokens_and_cost_to_a_counter_that_does_not_exist_yet` ve `…_leaves_the_run_count_where_the_root_put_it`. Sevk edilen sözleşmeye case eklediği için `kapi.py yayin --kuru` kapanışta koşar. Ayrıca dört sevk edilen `///` metni eski kuralı anlatıyordu ve düzeltildi (`QuotaConsumption.Runs`/`RunId`, `WebhookQuotaSummary.RunId`, `RunEventCustomTypes.QuotaThreshold`, `PublishThresholdToRunStream`).
5. **Webhook okuması yalnız webhook kayıtlıyken yapılır** (plan: "yalnız webhook için kalır"; koşul `_quotaEnforcer is null && _webhookPublisher is null` idi, artık `_webhookPublisher is null`). Açık Soru 4: `StartedRuns` kapısı korundu.
6. **Eski bir test yeniden adlandırıldı:** `QuotaNoticeOrderingTests.A_child_run_never_records_quota_consumption` → `…_with_no_tree_account_records_no_quota_consumption`. Kural değişti ("çocuk hiç yazmaz" artık yanlış); test aynı kalır (hesapsız çocuk yazmaz), iddia tam hâliyle `TreeQuotaTests`'tedir.
7. **Açık Soru 1-4 önerilen seçenekle (A) kapatıldı:** `AgentRunBudget` internal üyesi · usage'sız çocuk girdi yazmaz · geç girdi `RunId`'si çağıran run · `ReadTreeConsumptionAsync` yalnız webhook için kalır.
8. **Eşik bildirimi payload'ı** kiracı ve kullanıcıyı çocuğun kapsamından değil hesaptan alır (`WriteQuotaThresholdNoticeAsync`); çocuğun ortam kiracısı arka planda varsayılandır (taban sürümde `A_late_childs_entry_goes_to_the_roots_tenant` kırmızı).
9. **Plan tablosunda vaat edilen üç test yazılmadı, gerekçeyle:** (a) *kuyruklu run'ın ikinci denemesi önceki denemenin çocuğunu yeniden sayar* — yapısal olarak olamaz: her run yalnız kendini yazar, kökün girdisi deneme başına kendi harcamasıdır; eski çift sayım (ağaç okuması) kota yolundan çıktı. (b) *her kök yolu hesap taşır* — §205.2 ölçümü: `AgentRunScope` yalnız iki yerde kurulur (`RunRecordingAgent.Lifecycle.cs`, `WorkflowRunner.cs`), ikisi de `Account` atar; MCP, A2A, eval, replay, devam ve onay devamı derinlik 0 olarak aynı `PrepareRun` yolundan geçer. İki yer `QuotaWriteSiteTests.Every_place_that_opens_a_root_scope_names_the_account…` ile sabitlendi. (c) *iptal edilen çocuk "o ana kadar kaydedilen usage"yı yazar* — çocuğun girdisi `CompleteAsync`'in elindeki usage'dır (satırın kendi rakamı); iptalden sonra dönen bir model yanıtı satırda da yoktur, yani sayaçta da yoktur (değişmedi). Usage'sız iptal yazmaz (`A_child_the_deadline_cancels_before_it_spent_anything_writes_no_entry`).
10. **Bir kusur bulundu, çözülmedi → aday:** SQLite kota sayacı tutarı kayan noktayla toplar (**F-497**; sözleşme testinin ilk sürümü `0,1 + 0,2`'de düştü, binary-kesin tutarlara çevrildi). Dört aday daha: **F-498** (eşik denetimi girdi başına okur), **F-499** (sapma 2), **F-500** ve **F-501** (denetimden: geç tool'un hata dalı ve webhook'suz eşik).

## Bu Fazda Verilen Kararlar

- **K-990** (kategori: güvenlik, public-api; kullanıcı kararı): kota girdisi harcayan run başına yazılır; kimlik ağacın bütçesindeki `TreeAccount`'tan okunur; tek yazım yolu `TreeQuota.RecordAsync`; `run.*` webhook'u kök kapanış anının görüntüsü; eşik bildirimi eşiği geçiren run'ın akışına. K-913'ün kota yarısını ve K-974'ün "yalnız detaya" kuralının kota yarısını değiştirir.
- Kategori dışı (yalnız burada): Açık Soru 1-4'ün seçimi (Sapma 7) · para birimi kuralının biçimi (Sapma 2) · geç tool tutarının kaynağı (Sapma 3) · üç testin yazılmaması (Sapma 9).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan aynı gün yazıldı ve değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 9 (derleme/analyzer 4: IDE0032, RS0016, CA1826, CS1503 · test kırmızısı 4: sevk edilen belge dili ×2, eşik testinin yarışı, SQLite kayan nokta · denetim 🔴 1) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (mevcut hiçbir test değiştirilmeden kırmızı olmadı; bir test yeniden adlandırıldı, Sapma 6) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz henüz kapanmadı) |

## Denetim Bulguları

`faz-denetcisi` (taze bağlam, 2026-10-14). Taban sürümde sekiz yeni test kırmızıydı (sayaç 10, beklenen 20).

| Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 🔴 | Geç tutar, tool süresi içinde de raporlamışsa çift sayılır: `KeepLateToolAmount` mühürdeki rapora bakmaz (0,02 süresinde + 0,05 sonradan → sayaç 0,07, gerçek 0,05) | **gerçek** — birim testi yazıldı ve düştü (`0,05` döndü, `0,03` olmalıydı) | Düzeltildi: `TakeLateToolAmount` mühürdeki tutarı düşer, yalnız artışı verir; küçük ya da eşit rapor bir şey eklemez. 3 birim testi + `A_tool_that_reports_a_larger_total_after_its_run_closed_adds_only_the_difference` |
| 🟡 | Sevk edilen XML metinleri eski kuralı anlatıyordu (`QuotaConsumption.Runs`/`RunId`, `WebhookQuotaSummary.RunId`, `RunEventCustomTypes.QuotaThreshold`) | — | Düzeltildi (dört metin + `PublishThresholdToRunStream` açıklaması) |
| 🟡 | CHANGELOG eşiği yalnız "sonra biten çocuk"a bağlıyordu | — | Düzeltildi ("çocuk run"); eşzamanlı çocuk için ayrı case yazılmadı: her `CompleteAsync` kendi `scope.Writer`'ına yazar, geç çocuk testi aynı yolu koşar |
| 🟡 | Para birimi dalı testsizdi (çocuk token dışı tutarı) | — | Kural `QuotaAmount` statik yöntemine çıkarıldı; 3 test (token dışı tutar korunur, tutarsız çocuk para eklemez, kök ve fiyatsız dokunulmaz) |
| 🟡 | Planın üç hata modu yazılmadı | — | Gerekçelendi (Sapma 9); kök yolu `QuotaWriteSiteTests` ile sabitlendi |
| 🟡 | Eski kural: `MT-RET-033`, `runs.md:90` | — | Düzeltildi |
| 🟢 | Geç tool hata verirse raporladığı tutar girmez · geç tutarın eşiği webhook yoksa duyulmaz · F-499'un tersi · ADAYLAR tablo biçimi | — | **F-500**, **F-501**, F-499 genişletildi, biçim düzeltildi |

Triyaj notu: `faz-denetim` Adım 5.1 🔴 bulguların kullanıcıya sorulmasını ister. Bu bulgu bir testle **mekanik olarak yeniden üretildi** (`KR-05`: repro varsa gerçektir) ve kullanıcı "karşılaşılan defect'leri çöz" demişti; bu yüzden sorulmadan kapatıldı. Kapanış mesajı bunu bildirir.

## Sonraki Faza Devir Notu

Sıradaki faz yok: `YOL-HARITASI.md`'nin son satırı 205'tir; seçilmemiş işler `ADAYLAR.md`'dedir.

**Devralınan sözleşmeler**

- Kota her run'da yazılır. Kök `AgentRunBudget.Account`'u atar (`TreeAccount`: kiracı, kök adı, kullanıcı, `IQuotaRecorder?`); çocuk (`Runs = 0`), workflow adımı, workflow kökü (yalnız kendi payı) ve geç tool aynı hesaba tek yoldan, `TreeQuota.RecordAsync`'ten yazar. `grep -rn "new QuotaConsumption" src/` yalnız `TreeQuota.cs`'i göstermelidir (`QuotaWriteSiteTests` zorlar).
- `run.*` webhook'u kök kapanış anının görüntüsüdür; `ReadTreeConsumptionAsync` yalnız onun içindir.

**Bilinen tuzaklar**

- 🚨 Kota girdisinin kimliği (kiracı, ad, kullanıcı) ortamdan okunmaz: arka plan çocuğu kök bittikten sonra biter ve ortam kiracısı varsayılandır. Yeni bir yazan eklersen hesabı bütçeden al, `ITenantContext`'ten değil.
- 🚨 Geç tool tutarı çağrının kaydından okunursa iki kez sayılır; kaynak `NonTokenCostAccumulator.TakeLateToolAmount`'tır (yalnız mühürden sonra raporlananı, mühürdeki aynı çağrının tutarını düşerek verir).
- 🚨 Para birimi: kök kendi maliyetini olduğu gibi yazar, alt run'ın token maliyeti yalnız yapılandırılmış birimdeyse girer (F-499). `Tracon:Pricing:Currency` `null` iken etiketli bir maliyet alt run'da girmez.
- Ağaç başına en çok `MaxTotalRuns + 1` kota yazımı ve girdi başına eşik okuması vardır (F-498); ölçülmüş bir yük sorunu bildirilmedi.
- SQLite kota sayacı tutarı kayan noktayla toplar (F-497); sözleşme testlerinde ikili-kesin tutarlar kullan.
- Bu dosyayı bir betikle düzenlerken `## Sonraki Faza Devir Notu` dizisini başlık olarak **satır başıyla** ara: "Bu Faza Başlarken" listesindeki `awk` komutu aynı dizeyi taşır ve düz `index` onu bulup dosyayı keser (bu fazın kapanışında bir kez yaşandı, git'ten geri alındı).

**Yarım kalan işler**

- **Site yayını (`faz-tamamlama` Adım 10) koşulmadı**: `./scripts/site-deploy.sh` dış dünyaya yayın yapar; kullanıcı onayı bekler. Kapılar yeşil (`npm run check`).
- `MT-RET-082` (webhook alıcısı gerekir) ve `MT-RET-084` (zaman aşımına uğrayıp tutar bildiren örnek tool yok) elle koşulmadı; ikisinin otomatik karşılığı CI'dadır.
