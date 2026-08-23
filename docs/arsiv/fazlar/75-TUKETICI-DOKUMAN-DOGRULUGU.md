# Faz 75 — Tüketici Dokümanının Doğruluğu

> **Durum:** ✅ Tamamlandı (2026-08-20)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-126**, **F-124**
> **Önkoşul:** [Faz 74](74-YEREL-REFERANS-YUZEYI.md) — tüketici agent'ını paketlenmiş XML korpusuna yönlendiren faz budur; bu faz o korpusu okunabilir yapar · [Faz 73](73-TUKETICI-AGENT-DESTEGI.md) — harita üreteci ve cırcır kapısı deseni · [Faz 57](57-KOD-DILI-BIRLESTIRME.md) — `SourceLanguageTests` cırcır altyapısı ve K-408 dil sınırı
> **Paketler:** On yedi paketin tamamı (yalnız XML dokümanı ve `README.md`) · `docs-site/` · `tests/`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Büyümüyor.** XML doküman metnini değiştirmek imza değiştirmez. Ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` = 16 satır (16 paket × 1 boş satır)
> **Site etkisi:** Yeni sayfa `guides/coding-agents.md` + kenar çubuğu bölümü · `ui.md` · `guides/observability.md` · `reference/configuration.md` · `http-api.md` · `capabilities.md` · üretilen `AgentPrism.AgentMap.md` / `llms.txt` / `llms-full.txt` **revizyonu değişir**
> **Manuel test alanı:** `docs/manuel-test/31-DOKUMAN-DOGRULUGU.md` (30 numarayı Faz 74 aldı)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/75-TUKETICI-DOKUMAN-DOGRULUGU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 73 tüketicinin kod agent'ına **haritayı** verdi. Faz 74 onu paketin kendi diskindeki **ayrıntısına** yönlendirdi: 2,96 MB XML dokümanı, `~/.nuget/packages` altında. Bu faz o korpusu ilk kez **okurunun gözünden** ölçer ve üç şey bulur. Birincisi ve en ağırı: sevk edilen doküman **kendi kendine yetmiyor**.

## Bitiş Ölçütleri (DoD)

- [x] `dotnet pack` sonrası 15 paketin `lib/net10.0/*.xml` dosyalarında iç referans deseni **sıfır eşleşme** verir — ölçüldü (taban 1 033). Kalan tek eşleşme sıradan İngilizce'deki küçük harfli `rationale` kelimesidir, `Rationale:` etiketi değil
- [x] Paketlenen `buildTransitive/agentprism.json` **sıfır eşleşme** verir (taban 39); ayrıca 26 imza sızıntısı (`string? X.Y`) da sıfırlandı
- [x] 18 paket README'sinin 18'inde sıfır iç referans **ve** doküman sitesine en az bir bağlantı — ölçüldü: 0 ve 18/18
- [x] Kök `README.md` İngilizce; faz kayması yok; **17 701 / 20 000 bayt** (%11 boş)
- [x] `ShippedDocumentationSelfContainmentTests` yeşil; taban çizgisi **boş** (yalnız dört yorum satırı); iki yönde de kızardığı gösterildi
- [x] 11 bölümün **11'i** bir `Rule:` satırı taşır (ikisi hiç taşımıyordu). ~~`…` sıfır kez~~ → **Sapma 4:** üç kesme kalır ve hepsi **kelime sınırındadır**; kusur kesmenin kendisi değil, kelime ortasından kesmekti
- [x] `guides/coding-agents.md` yayında, kendi kenar çubuğu bölümünde; altı `APG` tanısı, iki MSBuild özelliği ve üç üretilen dosya anlatılıyor
- [x] **18** konsol gezinme girişinin 18'inin `ui.md`'de kendi başlığı ve bir görüntüsü var (plan 17 diyordu; ölçülen 18). Görüntü sayısı 14 → **19**
- [x] **34** telemetri adının 34'ü ve 266 `Options` property'sinin 266'sı en az bir sayfada geçer. **Sapma 5:** taban çizgisi dosyası yazılmadı — borç zaten sıfır olduğu için doğrudan iddia yeterli ve daha güçlü
- [x] `http-api.md` **160 operations across 123 paths** diyor ve kapıya bağlı (bayat değer 143/112 idi)
- [x] `check:content` 38 elle yazılmış / 1000 toplam sayfa temiz · `build` 1001 sayfa · `check-links` **128 663 bağlantı, kırık yok**
- [x] Dört doğrulama kapısı: `build` 0/0 · `test` **4 408 test, 0 başarısız** · `pack` temiz · `format --verify-no-changes` exit 0
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı — çıktı aşağıda
- [x] `secret` taraması: çıkan beş satırın hepsi test sabiti (`FAKE-VOICE-KEY-…`, `test-api-key-…`) ve analyzer testinin bilinçli girdisi
- [x] `docs/manuel-test/31-DOKUMAN-DOGRULUGU.md` yazıldı (**20 case**); 1–16, 19, 20 koşuldu, 17 ve 18 👤 işaretli
- [x] `faz-denetim` koşuldu; **6 🔴 · 8 🟡 · 2 🟢** bulgu çıktı, altı 🔴'ın altısı da kapandı; 🔴 kalmadı


### Gerçek çıktı — örnek uygulama

```text
GET  /agentprism/api/meta
  {"version":"0.0.0-preview.0.273","prefix":"/agentprism",
   "storage":{"persistent":true,"runStore":"SqlRunStore","jobWorkerEnabled":true}}

POST /agentprism/api/agents/claude-support/run    → SSE, 10 olay
  event: run     {"runId":"01a01c9e-a1f1-75c9-aa84-ad00e513a1d5"}
  event: update  ... (Anthropic gercek yaniti)
  event: done

GET  /agentprism/api/runs?limit=1
  {"id":"01a01c9e-a1f1-75c9-aa84-ad00e513a1d5","agentName":"claude-support","status":"Completed"}
```

Akışı erken kapatan ilk deneme `Canceled` kaydetti — doğru davranıştır ve
iptal yolunun canlı kanıtıdır.

### Gerçek çıktı — paketlenen yapıtlar

```text
dotnet pack AgentPrism.slnx -c Release        # 0.0.0-preview.0.273
15 nupkg → lib/net10.0/*.xml   ic referans: 0   (faz oncesi 1 033)
AgentPrism.AspNetCore.nupkg → buildTransitive/agentprism.json   ic referans: 0   (39)
15 nupkg → README.md           ic referans: 0   (14)
```

### Doğrulama komutları

```bash
# Sevk edilen XML temiz mi
dotnet pack AgentPrism.slnx -c Release
for p in artifacts/package/release/AgentPrism*.nupkg; do
  unzip -p "$p" 'lib/net10.0/*.xml' 2>/dev/null
done | grep -ciE "(phase|faz) [0-9]+|K-[0-9]{3}|F-[0-9]{2,3}|docs/"   # beklenen: 0

# Paketlenen OpenAPI temiz mi
unzip -p artifacts/package/release/AgentPrism.AspNetCore.*.nupkg \
  buildTransitive/agentprism.json | grep -ciE "phase [0-9]+|K-[0-9]{3}"  # beklenen: 0

# Harita kesme ve kural
grep -c "…" docs-site/public/llms.txt        # beklenen: 0
grep -c "^- Rule:" docs-site/public/llms.txt # beklenen: 11

# Kapinin gercekten yakaladigi gosterilir
dotnet test --filter ShippedDocumentationSelfContainment
cd docs-site && npm run check:content
```

---

## Plandan Sapmalar

> Uygulama sırasında keşfedildikçe yazılır; kapanışta tamamlanır.

**1 — İş hacmi 896 değil 1 243 satır; ikinci bir kusur sınıfı var.**
`faz-uygulama` Adım 1'in ölçümü planın sayısını düşürdü. Plan yalnız **iç
referansları** saydı (`phase 64`, `K-032`, `docs/NN-*.md`) ve **914** buldu.
Ölçüm ikinci bir sınıf gösterdi: sevk edilen XML dokümanı geliştirme
günlüğünün **sesiyle** yazılmış — **347** satır 🚨/⚠️ taşıyor, **98** satır
`Rationale:` ile açılıyor, dördü `Measured (2026-…)` bloğu. Birleşik küme
**1 243 satır**, 17 pakette.

Bu sınıf kapsam dışı **bırakılamaz**, çünkü [§75.3](#753--paketlenen-openapi-ve-sanitize-hasarı)
sanitize süzgecinin boş çalışmasını istiyor. Ölçüldü: süzgeç tek değil **iki**
kopyadır ve toplam **110 satırdır** —
[`build-api-reference.mjs:554-625`](../../../docs-site/scripts/build-api-reference.mjs#L554-L625)
(70 satır) ve [`build-http-api.mjs:492-517`](../../../docs-site/scripts/build-http-api.mjs#L492-L517)
(26 satır). İçlerinde `🚨|⚠️ → **Important:**`, `Rationale: → ''`,
`Measured (20… → ''` kuralları var; yani proje bu sesi **zaten** tüketiciye
uygun bulmuyor ve siteye çıkarken siliyor. Kaynak temizlenmezse süzgeç
kalır, kalırsa üç bozuk cümle sınıfı da kalır.

Sonuç: kural tek cümleyle genişledi — sevk edilen doküman kendi kendine
yetmekle kalmaz, **tüketicinin sesiyle** yazılır. Kapı iki deseni de tarar.
Fazın kazancı da büyüdü: 110 satırlık onarım zinciri **silinebilir** hâle
gelir.

**2 — Uygulama sırasında bulunan üçüncü kusur sınıfı: `<see cref>` paketlenen
belgede TAM İMZA olarak render ediliyor.** Ölçüldü: `docs/openapi/agentprism.json`
içinde önce **26 yerde** `string? ClientToolResult.ErrorMessage`,
`int? ModelBinding.MaxOutputTokens` gibi metinler bulundu. 🚨 İlk düzeltme yalnız
**nullable** şekli kapatıyordu; ikinci ölçüm **17 vaka daha** gösterdi
(`string AgentDefinition.Name`, `bool EvalCaseResult.Passed` — soru işareti yok,
sızıntı aynı). Kapı iki şekli birden arar; toplam 83 satırda `<see cref>` → `<c>`. Kaynağı AgentPrism değil, ASP.NET Core'un XML doküman
üretecidir — `<see cref="X"/>` cümlenin ortasına imzayı basıyor. Site kopyası
bunu bir süzgeç kuralıyla siliyor; paketlenen kopya silmiyor.

Çözüm bu 26 yerde `<see cref>` yerine JSON adını `<c>` ile yazmaktır: OpenAPI
belgesini okuyan tüketici zaten CLR imzasını değil JSON alanını görüyor, yani
metin hem düzelir hem doğrulaşır. Kapı: [§75.6](#756--yeni-içerik-kapisi-ailesi)
iddiası 5 bu deseni de arar.

**3 — Ekran görüntüsü listesi beş değil BEŞ + tohum.** Plan yalnız beş satır
eklemeyi yazıyordu. Ölçüldü: yeni beş ekranın dördü **boş durum** gösteriyordu
(`No schedule yet`, `No job yet`), ve testin kendi dokümanı "boş bir konsolun
ekran görüntüsü hiçbir şey öğretmez" diyor. `SeedCatalogAsync` eklendi: bir skill,
bir schedule, tetiklenmiş bir job, bir trigger ve bir MCP sunucusu kurulur; iki
`run` artık tek bir `sessionId` paylaşır, böylece oturum listesi de dolu gelir.
Tohumlama iki gerçek sözleşmeyi de ortaya çıkardı: `JobScheduleSaveRequest.Payload`
atanmazsa uç **500** döner (`JsonElement` `Undefined` tuzağı) ve tetikleyici
imzalama anahtarı `AgentPrism:TriggerSecrets:` önekini zorunlu tutar.

**4 — Kesme kusuru "sıfır `…`" değil, "kelime ortasından kesme" idi.** DoD
`…` karakterinin hiç geçmemesini istiyordu. Ölçüldü: harita satırı 52 karakterle
sınırlıdır ve üç açıklama o sınırı gerçekten aşıyor; kesmenin kendisi doğru
davranıştır. Kusur `provide…` · `core s…` gibi **kelime ortasından** kesmekti.
`shorten()` artık son boşluktan keser ve kesmeden önceki noktalamayı düşürür;
üç kesme kaldı ve üçü de kelime sınırında.

**5 — Telemetri ve `Options` kapıları taban çizgisi dosyası ALMADI.** Plan cırcır
deseni öngörüyordu. Uygulamada borç zaten sıfıra indiği için taban çizgisi boş
doğacaktı; boş bir taban çizgisi dosyası, doğrudan iddiadan **daha zayıftır**
(bir sonraki oturum ona satır ekleyebilir). İki kapı da istisnasız iddia eder.

**6 — İki yeni kapı ilk yazımda GEVŞEKTİ ve hiçbir şey yakalamadı.** Kanıtlama
turu gösterdi: konsol ekranı kapısı "sayfada adı geçiyor mu" diye soruyordu ve
`Jobs` kelimesi o ekranı hiç anlatmayan bir çapraz bağlantıda da geçiyordu;
telemetri kapısı `includes()` kullanıyordu ve `agentprism.tenant.id`,
`agentprism.tenant.identifier`'ın **ön ekidir**. Sıkılaştırılmış kapı **beş
gerçek boşluk daha** buldu: `ui.md`'de Tools, Skills, Models, MCP, Triggers,
Audit ve Diagnostics ekranlarının kendi başlığı yoktu. Ders: bir kapıyı yazdıktan
sonra **kırmızı olduğunu görmeden** yeşil kabul etme.

**7 — Depo kuralı ihlali değil ama kayda değer: `-p:AgentPrismFrontendEnabled=false`
ile tam yeniden derleme `AgentPrism.slnx`'ten `AgentPrism.UI` satırını sildi.**
İki kez gözlendi (bir kez de `AgentPrism.Ui.E2ETests.csproj`'un `ProjectReference`
satırı). Sonuç sessiz değildi ama teşhisi yanıltıcıydı: `AgentPrism.src.slnf`
bozuldu ve 80 test "başarısız" göründü. İstek üzerine **yeniden üretilemedi**;
artımlı derleme tetiklemiyor. Not olarak bırakıldı — mekanizma iddia edilmiyor.

## Bu Fazda Verilen Kararlar

| # | Karar |
|---|---|
| **K-514** | Sevk edilen dokümantasyon kendi kendine yeter: pakete giren bir metin yalnız tüketicinin elindeki şeylere gönderme yapar. Kural sesi de kapsar (🚨, `Rationale:`, `Measured (20…)`); `//` uygulama yorumları kapsam dışıdır |
| **K-515** | Sevk edilen dokümanı denetleyen cırcır **satırı değil bloğu** okur — XML yorumu sarıldığı için `(phase 65)` iki satıra bölünür ve satır bazlı tarama iki yarıyı da göremez |
| **K-516** | Site üreteçleri artık onarmaz, **hata verir**. ~110 satırlık zincir ölçülerek emekliye ayrıldı: kaldırılınca 689 sayfanın yalnız biri değişti |
| **K-517** | `<see cref>` paketlenen OpenAPI'de tam imza olarak render edilir; sözleşme tiplerinde `<c>ÜyeAdı</c>` yazılır |
| **K-518** | Kök `README.md` İngilizce'dir; depo içi `docs/` bağlantıları korunur |

Gerekçeler `docs/KARARLAR.md`'dedir.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir denetçiyle koşuldu (2026-08-20). Denetçi dört kapıyı
bağımsız koştu, üç üreteci ayrı ayrı çalıştırdı, paketlenen belgeyi ölçtü ve
`guides/coding-agents.md`'nin her iddiasını koda karşı doğruladı. **6 🔴 · 8 🟡 · 2 🟢.**
Altı 🔴'ın altısı da gerçekti.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | Mekanik `///` temizliği, §75.3'ün yok etmek için var olduğu **bozuk cümle sınıfını kaynakta yeniden üretti** — `made. <c>TenantId</c>,.`, `at all. Detail:.`, `promotion,.`, sarkan `See`, beş boş `<para></para>` | **Düzeltildi.** Ölçüm 35 vaka buldu (4'ü yanlış pozitif); hepsi metin olarak onarıldı, hiçbiri silinmedi. Onarım pasajı bir bloğu tek satıra çökertti — 119 satır yeniden sarıldı |
| 2 | 🔴 | **Bilgi kaybı:** `AgentPrismRunOptions.SessionId`'nin `<remarks>`'ından "oturumsuz yazılan bir attachment öksüz sayılır ve saklama politikası onu siler" cümlesi adresle birlikte **tamamen** silindi | **Düzeltildi.** Gerçek kendi `<para>`'sı olarak geri yazıldı. §75.1'in "içerik korunur, adres düşer" kuralının tek ihlaliydi |
| 3 | 🔴 | `<see cref>` → `<c>` düzleştirmesi **kendine gönderme yapan cümleler** üretti: `ContextWindowTokens`'ı belgelerken "taken from `ContextWindowTokens`" | **Düzeltildi.** Tarama 13 vaka buldu; her birinde tip niteleyicisi metin olarak geri kondu (`<c>ModelDescriptor.ContextWindowTokens</c>`). Ayrıca aynı sınıftan üç vaka daha bulundu ve düzeltildi |
| 4 | 🔴 | "Tam CLR imzası" sınıfı kapanmamıştı: **24 tanesi hâlâ paketlenen belgede** ve kapının regex'i onları göremiyordu (`?` zorunluydu) | **Düzeltildi** — denetim raporu gelmeden önce bağımsız olarak da bulunmuştu. Kapı iki şekli birden arar; ölçüm sıfır |
| 5 | 🔴 | **Tüketicinin kendi projesi** kapı kapsamının dışındaydı: `AgentPrism.Starter.csproj`, `Program.cs`, `Starter/README.md` ve iki `buildTransitive/*.targets` hâlâ `K-392`, `K-032`, `K1`, 🚨 taşıyordu | **Düzeltildi.** Dokuz satır temizlendi ve kapı kapsamı `/content/` ile `/buildTransitive/` altındaki her dosyayı **tam metin** tarayacak şekilde genişletildi |
| 6 | 🔴 | `reference/configuration.md` bir **güvenlik seçeneğinin varsayılanını yanlış** yazıyordu: `ClaimType` varsayılanı `tenant_id` değil **yok**; tablo `Enabled`'ı hiç anmıyordu | **Düzeltildi.** Varsayılan *(none)* olarak yazıldı, "ayarlanmazsa hiçbir claim okunmaz" uyarısı ve eksik `Enabled` satırı eklendi |
| 7 | 🟡 | Beş yeni ekranın landmark'ı **kenar çubuğu etiketiydi** — her rotada bulunur, hiçbir şey kanıtlamaz | **Düzeltildi.** Landmark artık tohumun yazdığı satırdır (`support-ord-7`, `nightly-summary`, `refund-policy`, `knowledge-base`, `helpdesk-webhook`); beşi de yalnız kendi ekranında görünür |
| 8 | 🟡 | `jobs.png` **yarış hâlindeydi**: job tetiklenip beklenmeden ekran alınıyordu | **Düzeltildi + yeni bulgu.** `WaitForJobToSettleAsync` job terminal duruma gelene kadar bekler, gelmezse **düşer**. Beklemek ikinci bir kusuru gösterdi: tohum `AgentRun` kullanıyordu ve o kind çağırandan `runId` bekler — job **`failed` durumdaydı**. `AgentBatch`'e çevrildi; görüntü artık `completed 1/1` |
| 9 | 🟡 | `Options` kapısı **çıplak sözcük** eşliyordu; dört üye ilgisiz bir cümle sayesinde yeşildi | **Düzeltildi.** Kapı artık üyeyi **sahip tipini veya bölüm adını da anan** bir sayfada arar. Sıkılaştırma **19 gerçek boşluk daha** buldu; hepsi dolduruldu |
| 10 | 🟡 | Desen **beş kopyada** ve ayrışmıştı; `hasInternalHistoryMarker` ölü koddu | **Düzeltildi.** Tek kaynak: `docs-site/scripts/internal-history.pattern`. Üç üreteç `internal-history.mjs` üzerinden, .NET kapısı dosyayı doğrudan okur. Ölü fonksiyon silindi. Açık Soru 1'in cevabı (A) böylece gerçekten uygulandı |
| 11 | 🟡 | DoD `…` sıfır diyordu, gerçek 3 | **Zaten kapalıydı** — Sapma 4 olarak yazılmıştı; kusur kesme değil kelime ortasından kesmeydi |
| 12 | 🟡 | `MT-DDG-007`'nin ön koşulu kendisiyle çelişiyordu: boş taban çizgisiyle cırcırın ikinci yönü tetiklenemez | **Düzeltildi.** Case üç adımlı yazıldı ve **gerçekten koşuldu**: `- …: 0 offending lines, baseline still allows 1 — refresh it` |
| 13 | 🟡 | İki kapı **sessizce atlıyordu**: camelCase bir `nav.*` anahtarı hiç eşleşmiyor, `agentprism.` öneki olmayan üç telemetri adı hiç taranmıyordu | **Düzeltildi.** Anahtar deseni `[A-Za-z]+`; telemetri taraması artık dosyadaki **her** sabiti okur. Üç ad (`execute_skill_script`, `compact_history`, `skill_script`) belgelendi |
| 14 | 🟡 | `guides/coding-agents.md` satır 169 hâlâ "both properties" diyordu | **Zaten kapalıydı** — denetim sırasında düzeltilmişti |
| 15 | 🟢 | 38 `<see cref>` → `<c>` dönüşümü "109 cross-reference rendered as code" sayısını büyütmüş olabilir; önce/sonra ölçülmedi | `ADAYLAR.md` · **F-128** |
| 16 | 🟢 | Ses deseni `Measured (20` büyük/küçük harfe duyarlıydı | **Düzeltildi** (🟢 olmasına rağmen ucuzdu): yalnız o alternatif `(?i:)` ile duyarsızlaştırıldı — `Rationale:`/`Decision:` **etiket** olduğu için duyarlı kaldı, yoksa sıradan İngilizce'deki `rationale:` yanlış pozitif verirdi. Altı satır temizlendi |

**Denetçinin temiz bulduğu başlıklar:** 3.5 (imza-gövde kayması — hiçbir imza
değişmedi) · 3.6 (plan dışı public API yok; 16 `PublicAPI.Shipped.txt` dosyasının
hiçbiri değişmedi) · senkronizasyon kopyası (K-411 sınıfı) · `secret` taraması ·
bağlantı doğruluğu (15 ayrık site adresinin hepsi çözülüyor, 18/18 README bağlantılı) ·
cırcır taban çizgisinin gerçekten boş olduğu — denetçi bunu **bağımsız grep ile**
doğruladı.

🚨 **Bu denetimin dersi tek cümledir: bir kusur sınıfını kapatan tur, aynı sınıfı
üretebilir.** Bulgu 1 tam olarak §75.3'ün yok ettiği şeydi ve kaynakta yeniden
doğmuştu; bulgu 3 ise düzeltmenin kendi yan etkisiydi. İkisini de yeni cırcır
görmedi çünkü o **desen** arar, **cümle bütünlüğü** aramaz. Altı 🔴 kapandıktan sonra
dört kapı yeniden koşuldu.

## Sonraki Faza Devir Notu

1. 🚨 **XML yorumunu SATIR SATIR tarama.** Yorum kaynak genişliğinde sarılır ve
   `(phase 65)` rutin olarak iki satıra bölünür; satır bazlı bir regex iki yarıyı
   da göremez. Ölçüldü: üç referans bu delikten geçti. `///` bloğunu birleştir,
   öyle eşleştir (K-515).
2. 🚨 **Bir kapıyı yazdıktan sonra KIRMIZI olduğunu gör.** İki yeni kapı ilk
   yazımda gevşekti ve hiçbir şey yakalamadı: "sayfada adı geçiyor mu" bir çapraz
   bağlantıyla tatmin oluyordu, `includes()` bir ön ek eşleşmesini kabul ediyordu.
   Sıkılaştırılınca **beş gerçek boşluk daha** çıktı. Yeşil bir kapı, çalışan bir
   kapı değildir.
3. 🚨 **Üretilen site sayfaları `.gitignore`'dadır** (`api/`, `http-api/`,
   `public/openapi/`). `git status` temizken üretilen içerik bayat olabilir; ve
   `build-api-reference.mjs --skip-docfx` **bayat `docfx/api-md` önbelleğini**
   okur. Kaynak XML'i değiştiren her turda tam koşum gerekir.
4. 🚨 **Metin onarım zinciri yazma — hata ver.** İki üreteçte 110 satırlık
   `replace` zinciri vardı ve tek işi kaynağın kirliliğini gizlemekti; üç bozuk
   cümle sınıfı da o zincirin ürünüydü. Kaynak temizlenince zincir ölçülerek
   kaldırıldı: 689 sayfanın yalnız biri değişti (K-516).
5. **Ekran görüntüsü tohumu artık katalog kurar** (`SeedCatalogAsync`): bir skill,
   bir schedule, tetiklenmiş bir job, bir trigger, bir MCP sunucusu; iki `run` tek
   `sessionId` paylaşır. Yeni bir ekran eklerken tohumu da büyüt — boş bir ekran
   görüntüsü kılavuzda hiçbir şey öğretmez.
6. **`docs/**.md` dizin bütçesi %2 boşluğa indi** (4 883 454 / 5 000 000).
   [Faz 76](76-DOKUMAN-KALITESI-VE-GORSEL-KIMLIK.md)'nın kapanış bölümleri bunu
   aşırır. Taşıma yapılmadı çünkü hangi birikimli anlatının arşive gideceği bir
   **kullanıcı kararıdır**; Faz 76 bunu ilk işi olarak sormalıdır.
7. **Sözleşme tiplerinde `<see cref>` kullanma** — paketlenen OpenAPI onu tam imza
   olarak basar (K-517). 🚨 **İki şekli vardır ve ilk düzeltme birini kaçırdı:**
   `string? X.Y` (nullable) ve `string X.Y` (düz). Nullable şekli kapatıldıktan
   sonra ikinci ölçüm **17 vaka daha** buldu. Kapı artık ikisini de arar. Sınır
   yalnız OpenAPI'nin seri hâle getirdiği tiplerdir; iç tiplerde `<see cref>` IDE
   gezinmesi için değerlidir.
8. **Faz 76 bu fazın beş içerik kapısının üstüne yazar.** Hiçbir düzenleme onları
   gevşetemez; kızaran bir kapı düzenlemenin kusurudur, kapının değil.
