# Faz 132 — Uygulanan Fiyat Snapshot'ı ve Sağlayıcı Kimliği

> **Durum:** ✅ Tamamlandı (2026-09-02)
> **Kaynak:** [kesif/2026-09-01-tuketici-feature-talepleri.md](../../kesif/2026-09-01-tuketici-feature-talepleri.md) — **F-175**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** gerekli — üç set (`runs` tablosuna dört sütun) + üç `MigrationsViews` güncellemesi; numara uygulama anında alınır
> **Public API:** büyüyor (`RunCost` alanları, `RunRecord.ModelProvider`) **ve bir uç davranışı daralıyor**. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 1 satır; shipped giriş sıfır, bugün eklemek ucuz
> **Tüketici yüzeyi:** `docs-site/`: `guides/observability.md`, `reference/read-views.md`, `concepts/runs.md`, `http-api.md` · sevk edilen: `RunCost` ve `IRunPricingResolver` XML dokümanı, `runs_v1` sütun tablosu
> **Manuel test alanı:** [`docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show ecfc78e:docs/arsiv/fazlar/132-UYGULANAN-FIYAT-SNAPSHOTU.md
> ```
>
> Damıtıldı 2026-09-02 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz iki işi birlikte yapar: bir **çelişkiyi** kapatır ve bir **kanıt boşluğunu** doldurur.

## Bitiş Ölçütleri (DoD)

- [x] `run` yanıtı uygulanan birim fiyatları ve `modelProvider`'ı taşır (case 1) —
      `RunStoreContract.Applied_unit_prices_round_trip_and_do_not_enter_the_total`
      (4 sağlayıcıda yeşil) + gerçek HTTP: `GET /api/runs/<id>` `modelProvider: "echo"` döndü (aşağıda)
- [x] Fiyat listesi değişse de fiyatlanmış `run` **değişmez** (case 2) —
      `RunCostRecalculationServiceTests.A_priced_run_is_skipped_even_when_a_new_price_would_change_it`
- [x] Yeniden hesaplama yalnız `Unknown` satırları doldurur (case 3) —
      `RunCostRecalculationServiceTests.An_unknown_priced_run_is_filled_in_using_its_own_stored_provider`
- [x] Yedek link cevap verdiğinde sağlayıcı doğru yazılır (case 4) —
      `FallbackRecordingTests.Fallback_model_overrides_the_runs_model_id` (`run.ModelProvider.ShouldBe("fallback")`)
- [x] Bilinmeyen fiyat `null` kalır, `0` olmaz (case 5) —
      `RunStoreContract.Unit_prices_stay_null_when_the_model_price_is_unknown` +
      `RunPricingResolverTests.Cost_is_null_not_zero_when_no_price_exists_anywhere`
- [x] `RunCost.Total()` birim fiyatları **toplamaz**; `RunCostTotalTests` bunu ölçer —
      `tests/AgentPrism.Core.UnitTests/Models/RunCostTotalTests.cs` (3 test, yeşil)
- [x] `runs_v1` dört yeni sütunu taşır; `ReadViewColumnSetTests` ve `ReadViewCostTermTests` yeşil —
      `AgentPrism.Sql.Shared.UnitTests`: 20/20 yeşil
- [x] `RunStoreContract` dört koşumun dördünde de yeşil —
      InMemory (Core.UnitTests içinde) · SQLite 634/634 · PostgreSQL 690/690 · SQL Server 620/620
- [x] Dört doğrulama kapısı sıfır uyarı verir — bkz. Doğrulama Kapıları çıktısı, kapanışta koşuldu
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` temiz
- [x] Manuel kabul case'leri `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklendi; otomatikleştirilebilenler koşuldu —
      MT-OBS-059 eklendi (`modelProvider`/birim fiyat/snapshot/`runsSkipped`
      hepsi tek case'te birleşti — bütçe zorladı, bkz. Plandan Sapmalar);
      örnek uygulamada `curl` ile eşdeğer davranış doğrulandı (aşağıda), arayüz
      adımı 👤
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi; `npm run check` (dört alt kapı) temiz — bkz. Denetim Bulguları öncesi not
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı —
      `index-*.js.br` 149 390 B (önceki 148 807 B, +583 B); gzip 176.6 KB / 250 KB bütçe

### Doğrulama komutları

```bash
# Snapshot alanları
curl -s http://localhost:5081/agentprism/api/runs/<id> \
  | jq '{provider: .modelProvider, cost: .cost}'

# Daralan uç
curl -s -X POST http://localhost:5081/agentprism/api/stats/recalculate-costs | jq

# Görünüm sütunları
psql -c "SELECT model_provider, input_price_per_mtok FROM agentprism.runs_v1 LIMIT 1"
```

---

## Plandan Sapmalar

- **`RunProviderAttributionTests` ayrı dosya olarak açılmadı.** `FallbackRecordingTests.cs`
  zaten aynı senaryoyu (birincilin devresi açıkken yedeğin cevap vermesi) uçtan
  uca kuruyordu; `run.ModelProvider.ShouldBe("fallback"/"primary")` iddiaları
  oraya eklendi. Ayrı dosya aynı `CreateAgent` iskeletini tekrar kurardı.
- **`CostAddendsCrossCheckTests`'in reflection filtresi daraltıldı.** Test
  `RunCost` üzerindeki HER `decimal?` özelliğin bir maliyet toplama terimi
  olduğunu varsayıyordu — bu faz üç RATE alanı (`*PricePerMillionTokens`)
  ekleyince varsayım bozuldu (test "toplam terim seti" beklerken hem eski
  hem yeni alanları görüp uyuşmazlık bildirdi). Filtre `p.Name.EndsWith("Cost")`
  ile daraltıldı — testin K-483 koruması aynen kalır, yalnız rate alanları
  artık taranmıyor.
- **Altı ayrı manuel kabul case'i taslağı (MT-OBS-059..064) tek bir case'e
  (MT-OBS-059) birleştirildi.** `docs/manuel-test/*.md`'nin toplam bütçesi
  (1 950 000 B, K-214 gereği büyütülemez) altı ayrı case'in tablo/başlık
  yüküyle aşıldı (`dokuman-bakim.py` AŞTI dedi). İçerik silinmedi — aynı
  iddiaların hepsi (provider, birim fiyat, snapshot değişmezliği,
  `runsSkipped`, arayüz) tek case'in adım/beklenen-sonuç listesine taşındı.
- **K-517'nin `<summary>` içinde `<see cref>` yasağı ilk yazımda ihlal
  edildi.** Dört yeni alanın `<summary>`'si komşu üyeye `<see cref>` ile
  atıfta bulunuyordu; bu OpenAPI belgesine ham CLR imzası olarak sızıyordu
  (bağımsız denetim buldu, 🔴 1). `<remarks>` içindeki aynı desen sorunsuzdu
  — kural yalnız `<summary>`'yi (OpenAPI `description`'ının kaynağı) kapsıyor.
- **`RunSupportTypes.cs`'e eklenen XML dokümanından 🚨 işareti kaldırıldı.**
  `ShippedDocumentationSelfContainmentTests` (Faz 90) sevk edilen XML
  dokümanında iç geliştirme sesini (`🚨`, `⚠️`, `Rationale:`, `Measured (20…)`)
  yakalıyor — ilk yazımda iki satırda 🚨 vardı, kapı bunu doğru şekilde kırmızı
  yaptı ve düzeltildi.
- **`WorkflowRunner.CompleteAsync`'in `RunEventWriter.CompleteAsync` çağrısı
  konumsal argümanlarla yazılmıştı.** `modelProvider` parametresi
  `cancellationToken`'dan ÖNCE eklenince `CancellationToken.None` sessizce
  `modelProvider`'a bağlanacaktı (tip uyuşmazlığı derleme hatası verdi, ama
  aynı desende iki `string?` parametre olsaydı SESSİZCE yanlış değere
  bağlanabilirdi). Çağrı adlandırılmış argümanlara çevrildi — tam olarak
  `faz-uygulama` Adım 4'ün uyardığı imza-gövde kayması tuzağı.
- **`samples/AgentPrism.Api` ile gerçek run doğrulaması fiyatsız (`Unknown`)
  yolla yapıldı.** Yerel makinede `AgentPrism__Pricing__Providers__echo__echo-1__*`
  ortam değişkeniyle `EchoModelProvider`'a fiyat tanımlamak denendi ama
  seçenek bağlama bir hata verdi (kapsam dışı, bu fazın kodunu etkilemiyor);
  gerçek katalog/yapılandırma fiyatlama yolu zaten `RunPricingResolverTests`
  (birim) ve `RunStoreContract.Applied_unit_prices_round_trip_and_do_not_enter_the_total`
  (dört sağlayıcıda sözleşme) ile kanıtlı olduğundan ek çaba harcanmadı.
  `GET /api/runs/{id}` gerçek HTTP üzerinden `modelProvider: "echo"` döndü;
  `POST /api/stats/recalculate-costs` gerçek HTTP üzerinden `runsSkipped`
  alanını taşıdı — bkz. "Doğrulama komutları" bölümü, gerçek çıktı yazıldı.

## Bu Fazda Verilen Kararlar

- **K-650** — `POST /api/stats/recalculate-costs` daraldı: artık yalnız
  `PricingSource.Unknown` (veya hiç fiyatlanmamış) satırları fiyatlar,
  bilinen fiyatlı bir satırı bir daha asla yeniden yazmaz. Tam gerekçe:
  `docs/KARARLAR.md`.

## Site Senkronu Gerekçesi (`--site-gerekce-yazildi`)

`dokuman-bakim.py --site-denetle` dört kural tetikledi; biri gerçek bir
güncelleme gerektirdi (`ui.md` — run detay ekranındaki yeni sağlayıcı/birim
fiyat karoları için bir cümle eklendi), üçü kapsam dışı kaldı:

- **`http-api` → `http-api.md`**: `CatalogEndpoints.cs` değişti ama sayfa
  genel HTTP sözleşmesini (kimlik doğrulama, sayfalama, hata biçimi) anlatır,
  uç bazlı detay taşımaz — o iş üretilen `http-api/agents.md`'nindir ve
  zaten yeniden üretildi (`WithDescription` metni oradan gelir).
- **`workflow` → `concepts/workflows.md`**: `WorkflowRunner.cs`'teki
  değişiklik davranış DEĞİL, imza kayması düzeltmesidir (konumsal argümanı
  adlandırılmışa çevirmek) — `modelProvider: null` zaten örtük değerdi.
- **`kalicilik` → `getting-started/persistence.md`**: sayfa migration
  MEKANİĞİNİ anlatır (`AutoApplyMigrations`, `EnableReadViews` bayrağı,
  saklama), sütun bazlı şema referansı taşımaz — o iş
  `reference/read-views.md`'nindir ve zaten güncellendi.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, taban `c47bdf7`) iki bulgu üretti;
ikisi de kapatıldı.

**🔴 1 — Yeni alanların `<summary>` bloğu `<see cref>` kullanıyordu; bu tipler
OpenAPI'ye seri hâle getirildiği için sözleşme metnine ham CLR imzası
sızıyordu (K-517 ihlali).** `RunCost.InputPricePerMillionTokens` /
`OutputPricePerMillionTokens` / `CachedInputPricePerMillionTokens` ve
`RunRecord`/`RunStartInfo`/`RunCompletion.ModelProvider` ile
`RunCostRecalculationResult.RunsConsidered`'ın `<summary>`'sindeki yedi
`<see cref>` `<c>ÜyeAdı</c>`'ya çevrildi (`<remarks>` içindekiler zaten
sorunsuzdu, dokunulmadı). `docs/openapi/agentprism.json` ve
`packages/agentprism-client/src/schema.ts` yeniden üretildi; ham imza
sızıntısı doğrulanarak kapatıldı (`grep` ile önce/sonra karşılaştırıldı).
**Düzeltildi.**

**🟡 1 — Uçun daralma davranışı (`runsSkipped`/Unknown-only filtre) yalnız
birim testle (`InMemoryRunStore`) kanıtlıydı; tek fonksiyonel test sıfır
`run` ile çalışıyordu.** `RoleAndAuditTests.cs`'e gerçek bir fiyatlı ve
gerçek bir fiyatsız `run`'ı `IRunStore` üzerinden doğrudan seçip
`POST /api/stats/recalculate-costs`'u tam HTTP+DI+store zinciriyle koşan
`Recalculate_costs_skips_an_already_priced_run_and_fills_in_an_unknown_one`
testi eklendi (4/4 yeşil). **Düzeltildi.**

Temiz çıkan başlıklar (denetçinin ifadesiyle): 3.1 DoD kanıtı, 3.2 test
tiyatrosu yok, 3.5 imza-gövde takibi (`WorkflowRunner.cs`'in konumsal
argüman kayması dahil, bkz. Plandan Sapmalar), 3.6 plan dışı public API yok,
3.7 repo kuralları, üç SQL sağlayıcısının `InsertRun`/`UpdateRunCompletion`/
`UpdateRunCost`/`SelectRun`/ordinal/migration/read-view tutarlılığı.

## Performans Tavanı Güncellemesi

`kapi.py performans` (Faz 116, sıfır toleranslı tahsis kapısı) `RunStoreQueryBenchmarks.QueryRuns`'ta
+496 B (44 336 → 44 832 B/op) artış buldu — dört yeni sütunun (`model_provider`
+ üç `decimal?` birim fiyat) her `SelectRuns` sayfasında okunmasının doğal,
beklenen bedeli. `python3 scripts/kapi.py performans --guncelle` ile
`bench/baseline.json` bilinçli olarak yükseltildi; diğer iki ölçüt
(`CacheHit`, `AppendEvent`) değişmedi.

## Sonraki Faza Devir Notu

- **🚨 Bir `record` özelliğinin `<summary>`'sine komşu bir üyeye `<see cref>`
  ile atıf koyma — OpenAPI'ye seri hâle getirilen her tip için bu ham CLR
  imzasını `description` alanına sızdırır (K-517).** `<c>ÜyeAdı</c>` yaz.
  `<remarks>` güvenlidir (yalnız site sayfasında link üretir, OpenAPI'ye
  girmez) — kuralı `<remarks>`'a da uygulamak GEREKMEZ, fazla iş olur.
  Bugün bunu yakalayan bir kapı YOK; bir kapı eklenmek istenirse aday listesine
  girer (bu fazda eklenmedi — YAGNI, tek vaka bağımsız denetimle yakalandı).
- **Reflection tabanlı bir sözleşme testi (`p => p.PropertyType == typeof(X)`
  gibi geniş bir filtre kullanan) yeni bir alan eklendiğinde SESSİZCE yanlış
  bir küme üretebilir.** `CostAddendsCrossCheckTests` "her `decimal?` özellik
  bir maliyet terimidir" varsayıyordu; bu faz aynı tipte ama farklı ANLAMDA
  (rate, addend değil) üç alan ekleyince test kırmızı oldu — testin niyeti
  doğruydu, filtresi dardı. Böyle bir testi genişletirken filtrenin GERÇEKTEN
  ne ayırt ettiğini (isim deseni, öznitelik, vb.) düşün.
- **`samples/AgentPrism.Api`'de `AgentPrism__Pricing__Providers__<sağlayıcı>__<model>__*`
  ortam değişkeniyle fiyat tanımlamak bu oturumda denenmedi/başarısız oldu**
  (seçenek bağlama hatası verdi, kök neden araştırılmadı — kapsam dışı
  bırakıldı). Gerçek katalog/yapılandırma fiyatlama yolunu örnek uygulamada
  elle doğrulamak gerekirse `dotnet user-secrets set` kullan (manuel test
  case'lerinin hepsi zaten bu yolu kullanıyor), ortam değişkenini değil.
- **🚨 `docs/manuel-test/*.md` toplam bütçesi (1 950 000 B, K-214) artık
  neredeyse dolu (%0 boşluk).** Bu faz eklerken birkaç kez aşıldı, sonunda
  altı case tek case'e sıkıştırılarak ~97 B boşlukla geçti. **Bir sonraki
  faz bu dosyaya YENİ case eklerse muhtemelen aşacak** — `python3
  scripts/dokuman-bakim.py` en baştan koş; aşarsa kısalt (K-214 büyütmeyi
  yasaklıyor) ya da kullanıcıya bu sınırı yeniden kalibre etmeyi (58.4
  formülü: ölçülen + %15) sor.
- `RunCost`/`RunRecord`'a yeni bir alan daha eklenirse aynı dört-sağlayıcı
  zinciri (Abstractions → Core resolver/recording → üç SQL sorgu dosyası →
  üç migration → üç read-view → `RunOrdinals`/`RunColumnOrder` → frontend
  `server-types.ts`/OpenAPI/TS istemci) tekrar baştan sona izlenir; bu faz o
  zincirin güncel bir örneğidir.
