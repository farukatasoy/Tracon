# Faz 35 — Maliyet ve Kota Metrikleri

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-70**
> **Önkoşul:** Yok. Faz 20 (maliyet) ve Faz 21 (kota) hazır veriyi üretiyor
> **Paketler:** `Tracon.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — iki enstrüman adı ve bir ayar

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/35-MALIYET-VE-KOTA-METRIKLERI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`TraconMetrics` beş enstrüman taşır ve **hiçbiri maliyet değildir**. Faz 20 maliyeti hesaplıyor ama yalnız `runs` tablosuna yazıyor. Grafana veya Azure Monitor'da maliyet panosu kurmanın yolu bugün veritabanını sorgulamaktır ve bu, tüketicinin APM'ine girmez. - **F-70** — `tracon.run.cost` sayacı (para birimi etiketiyle) ve `tracon.quota.usage` gözlemlenen ölçeri.

## Bitiş Ölçütleri (DoD)

- [x] Bir çalıştırma sonrası `tracon.run.cost` ölçümü `MeterListener` ile
      görülür ve değeri `runs` satırındaki maliyetle **eşittir** —
      `RunCostMetricTests.Fiyat_tanimliyken_maliyet_yayilir_ve_etiketler_kararlidir`
      (birim) + `RunCostMetricEndToEndTests` (gerçek DI + gerçek HTTP)
- [x] Etiket kümesi `agent.name`, `model.id`, `tenant.id`, `currency`'dir;
      `run.id` **yoktur** — aynı testte doğrulanır (tam 4 anahtar denetimi)
- [x] Kök + iki alt çalıştırmada sayaç toplamı ağaç maliyetini **çift saymaz** —
      `RunCostMetricTests.Kok_ve_iki_alt_calistirmada_sayac_agac_toplamini_cift_saymaz`
      (üç bağımsız `TraconRunOptions.Depth` çağrısı, toplam üç KENDİ maliyet)
- [x] `EnableQuotaUsageGauge` varsayılan `false`; açılmadan hiçbir kota sorgusu
      çalışmaz —
      `QuotaUsageObserverTests.Kapaliyken_hicbir_olcum_uretilmez_ve_depoya_gidilmez`
- [x] Ölçer açıkken ardışık on yoklama **bir** veritabanı sorgusu üretir —
      `QuotaUsageObserverTests.Ardisik_on_yoklama_bir_veritabani_sorgusu_uretir`
- Prometheus exporter ile `tracon_run_cost_total` metriği görünür —
      **doğrulanmadı**: örnek uygulamada hiçbir OTel exporter'ı (konsol/Prometheus)
      hiç kurulu değildi (bu fazdan önce de yoktu) ve bunu eklemek fazın
      ilan edilen kapsamının ("iki enstrüman adı + bir ayar, yeni uç yok") dışına
      taşardı. Enstrümanın gerçekten yayıldığı `MeterListener` ile (yukarıdaki iki
      madde) kanıtlanmıştır — Prometheus'un adlandırma dönüşümü
      (`tracon.run.cost` → `tracon_run_cost_total`) kütüphanenin değil,
      `OpenTelemetry.Exporter.Prometheus.AspNetCore`'un sorumluluğundadır ve test
      edilmeden kabul edilebilir bir üçüncü taraf sözleşmesidir.
- [x] Dört doğrulama kapısı sıfır uyarı verir — `SqlServer.IntegrationTests`
      hariç (bu makinenin Docker Desktop'ında `linux/amd64` imajları için Rosetta
      emülasyonu çalışmıyor; `docker run` ile doğrudan denendi, aynı host hatası —
      kodla ilgisiz, önceden var olan ortam sınırlaması)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, metrik çıktısı bu
      belgeye yazıldı — yukarıdaki "Doğrulama komutları" bölümü
- [x] `secret` taraması boş döndü

### Doğrulama komutları

> 🚨 **Plandaki komut yanlıştı** — `echo` bir **agent** adı değil, örnek
> uygulamanın ağa çıkmayan **model sağlayıcısının** adıdır
> (`GET /tracon/api/models` → `"name":"echo"`). Gerçek agent adı `support`'tur.
> Ayrıca bu sağlayıcının modeli (`echo-1`) fiyatsızdır — gerçek bir maliyet
> sayısı görmek için ya bir OpenAI API anahtarı tanımlanmalı ya da
> `Tracon:Pricing` altına `echo-1` için bir fiyat yazılmalıdır. Aşağıdaki
> komutlar **gerçekten çalıştırıldı** (2026-08-06) ve gözlenen çıktı budur.

```bash
curl -s -X POST http://localhost:5081/tracon/api/agents/support/run \
  -H 'content-type: application/json' \
  -d '{"message":"merhaba"}' --max-time 10

curl -s "http://localhost:5081/tracon/api/runs?agentName=support" | python3 -m json.tool
```

Gözlenen `runs` kaydı (fiyatsız sağlayıcı → `usage`/`cost` `null`, hata yok,
çalıştırma `Completed`):

```json
{
  "id": "019fd74d-220f-7e3b-841c-d9adc047f1c2",
  "agentName": "support",
  "status": "Completed",
  "modelId": "echo-1",
  "usage": null,
  "cost": null,
  "treeCost": null
}
```

`tracon.run.cost`'un GERÇEK bir fiyatla yayıldığı, MeterListener ile
ölçüldüğü ve K-151 (ağaç toplamını çift saymadığı) kanıtı otomatik testtedir:
`tests/Tracon.AspNetCore.FunctionalTests/RunCostMetricEndToEndTests.cs` —
`AddTracon()`'in GERÇEK DI zinciriyle (elle kurulmuş bir `RunRecordingAgent`
değil) sabit fiyatlı bir sahte sağlayıcı üzerinden çalıştırılıp doğrulanır. Bu
sandbox ortamında canlı bir OpenAI anahtarı yoktu; bu yüzden sayısal kanıt
manuel `curl` yerine bu testten alınmıştır (Faz 28 dersi: "bir prob programı
gerçek boru hattını kanıtlamaz" — `TestHost` + gerçek DI, izole bir prob
DEĞİLDİR).

```bash
# Prometheus exporter kuruluysa (bu fazda ornek uygulamaya eklenmedi — kapsam disi)
curl -s http://localhost:9464/metrics | grep tracon_run_cost

# 🚨 run.id etiket OLMAMALI — cikti bos donmelidir
curl -s http://localhost:9464/metrics | grep tracon_run_cost | grep 'run_id' \
  && echo "KARDINALITE HATASI" || echo "temiz"
```

---

## Plandan Sapmalar

1. **Kota ölçerine dördüncü bir etiket eklendi (`quota.metric`)** — plan yalnız
   `tenant.id`/`quota.scope`/`quota.period` öngörüyordu ama `QuotaDefinition`
   aynı anda `MaxRuns`+`MaxTokens`+`MaxCost` taşıyabilir; etiketsiz üçü aynı
   seride karışırdı. Kullanıcıya soruldu, "ekle" seçildi (K-254).
2. **Tek "oran" yerine iki ayrı mutlak ölçer** (`tracon.quota.usage` +
   `tracon.quota.limit`) — planın kendi önerisiyle aynı yönde ama taslak
   kod bloğu yalnız TEK bir sabit (`QuotaUsageGaugeName`) tanımlıyordu; bu
   çelişki kullanıcıya soruldu ve "ikisi de" onaylandı (K-255).
3. **`QuotaUsageObserver` arka plan zamanlayıcısı (`BackgroundService`/
   `PeriodicTimer`) DEĞİL, `ObservableGauge` geri çağırması içinde senkron
   kapılı (gated) bir tazelemedir** — plan "önbellek arka planda tazelenir"
   diyordu. `JobWorkerBackgroundService` deseni denendi ama net8.0 hedefi
   `PeriodicTimer(TimeSpan, TimeProvider)`/`System.Threading.Lock`'u
   kullanamıyor ve mevcut `ManualTimeProvider` sahtesi `CreateTimer`'ı
   override etmiyor — bu, arka plan zamanlayıcılı bir tasarımı testte
   ilerletilemez hâle getirirdi. Sonuç DoD ile birebir aynıdır (K-256).
4. **Kota ölçeri yalnız `ITenantStore`'a KAYITLI kiracıları tarar** — plan bunu
   hiç ele almamıştı. `IQuotaStore`'da çapraz kiracı listeleme yoktur (yalnız
   `ListAsync(tenantId)`); eklemek üç SQL sağlayıcısına dokunup fazın "yeni
   uç/tablo yok" sınırını aşardı. `QuotaEnforcer`'ın kendisi etkilenmez —
   yalnız gösterge panosu görünürlüğü kısıtlıdır (K-257).
5. **DoD'nin Prometheus doğrulama adımı çalıştırılamadı** — `samples/Tracon.Api`
   hiçbir zaman bir OTel exporter'ı (konsol veya Prometheus) kurmamıştı; bunu
   eklemek fazın "yeni uç yok" kapsamını aşardı. Enstrümanın gerçekten
   yayıldığı `MeterListener` ile kanıtlandı (bkz. testler); Prometheus adı
   dönüşümü üçüncü taraf paketinin sorumluluğudur.
6. **DoD'nin örnek komutu yanlıştı** — `echo` bir agent adı değil, model
   sağlayıcısının adıdır; gerçek agent `support`. Doküman düzeltildi.
7. **`SqlServer.IntegrationTests` bu oturumda doğrulanamadı** — bu geliştirme
   makinesinde Docker Desktop, `linux/amd64` SQL Server imajını Rosetta
   olmadan çalıştıramıyor (`docker run` ile doğrudan denendi, aynı host
   hatası verdi: *"Rosetta is only intended to run on Apple Silicon..."*).
   Faz 35 hiçbir SQL Server koduna dokunmuyor; kalan sekiz test projesi
   (Core, FunctionalTests, PostgreSql, Sqlite, Workflows, Mcp, Voice, OpenAI,
   Anthropic, Google, Azure, E2E) tam yeşildir.
8. **`scripts/dokuman-bakim.py` — `KARARLAR-INDEKS.md` üretimi "L{no}" önekini
   bıraktı, çıplak satır numarası yazıyor** — bu 4 yeni kararın eklenmesi
   `docs/KARARLAR-INDEKS.md`'yi bütçenin (25.000 bayt) 426 bayt üzerine
   çıkardı. K-214 bir dahaki aşımda **bütçe büyütülmeyeceğini, yapısal çözüm
   uygulanacağını** kaydetmişti; "L" öneki yalnız kozmetikti ("Satır" sütun
   başlığı zaten anlamı taşıyor) ve 257 satırda ~257 bayt kazandırdı — kalan
   fark yeni kararların başlıkları kısaltılarak kapatıldı. Kod tarafında etki
   yok, yalnız `docs/KARARLAR-INDEKS.md`'nin "Satır" sütunu artık `sed -n
   'N,Np'`ye doğrudan yapıştırılabilir (önceden "L120" yazıyordu, "L" elle
   silinmesi gerekiyordu).
9. **`docs/hafiza/kod-haritasi.md` içindeki senkronizasyon-kopyası tuzağı bu
   fazda tekrar yaşandı** — `src/Tracon.UI/wwwroot/index 2.html` ve
   `assets/*.css 2.br` sessizce `dotnet build`'i (arayüz derlemesi dahil)
   yeşil gösterip E2E testlerinin 41'ini de zaman aşımıyla düşürdü. `artifacts/`
   ve `wwwroot/` (ikisi de gitignore'lu, üretilmiş çıktı) tamamen silinip
   yeniden derlenerek çözüldü — bkz. MEMORY.md, Faz 30 dersiyle **aynı** kalıp.

## Bu Fazda Verilen Kararlar

K-254, K-255, K-256, K-257 — bkz. `docs/KARARLAR.md` Bölüm 2.

## Sonraki Faza Devir Notu

Faz 36 (Saklama Hacim Sınırı) bu fazın **hiçbir çıktısına bağımlı değildir** —
kendi önkoşulu Faz 25'tir. Bu bölüm yalnız genel bir özet taşır.

### Devraldığı sözleşmeler (gerçekleşen public API)

Yukarıdaki "Gerçekleşen Public API" bölümüne bakınız. Özet: `TraconMetrics`
artık altı enstrüman taşır (beş eskiden + `RunCost`); `TraconDiagnostics`
iki yeni gauge adı ve dört yeni etiket sabiti taşır.

### Bilinen tuzaklar

- 🚨 **`ObservableGauge` geri çağırması es zamanlıdır — içine `await` konamaz.**
  Veritabanı okuyan bir gözlemlenen ölçer yazacaksan `QuotaUsageObserver`'daki
  `Snapshot()` desenini izle: `SemaphoreSlim` ile korunan, `TimeProvider`
  karşılaştırmalı bir onbellek + yalnız bayatladığında BİR kez blok olarak
  (`GetAwaiter().GetResult()`) tazeleme.
- 🚨 **`ITenantStore` kaydı zorunlu değildir — çapraz kiracı taraması için
  güvenilir bir kaynak DEĞİLDİR.** `IQuotaStore`'da "tüm kiracıları listele"
  yoktur; yalnız `ListAsync(tenantId)` var. Bir gösterge/rapor tüm kiracıları
  taramak istiyorsa bu boşluğu bilerek kabul et veya `IQuotaStore`'a yeni bir
  üye ekle (üç SQL sağlayıcısına da dokunur).
- **Yeni bir metrik/etiket eklerken `MetricCardinalityTests`/benzeri bir
  denetim yaz** — `run.id`/`experiment_id`/`variant` hiçbir zaman etiket
  olmaz (K-146); yeni bir etiket sınırsız büyüyen bir alan taşımamalı.
- Senkronizasyon-kopyası tuzağı (`* 2.*`) bu fazda üçüncü kez yaşandı. Bir
  E2E/UI testi anlamsızca timeout ile düşerse önce
  `find . -name "* 2.*" -not -path "*/node_modules/*" -not -path "*/.git/*"`
  çalıştır.

### Yarım kalan işler

- Örnek uygulamada (`samples/Tracon.Api`) hâlâ hiçbir OTel exporter'ı
  (konsol/Prometheus) kurulu değil — Faz 6'dan beri böyle. Bu, Faz 35'in
  kapsamı dışında bırakıldı (bkz. Plandan Sapmalar #5) ama gözlemlenebilirlik
  fazlarının vaadini göstermek isteyen bir sonraki faz bunu ele alabilir.
- `SqlServer.IntegrationTests` bu geliştirme makinesinde koşamıyor (Docker
  Desktop Rosetta sınırı). CI ortamında (gerçek Linux/amd64) koşacağı
  varsayılıyor ama bu oturumda doğrulanamadı.
