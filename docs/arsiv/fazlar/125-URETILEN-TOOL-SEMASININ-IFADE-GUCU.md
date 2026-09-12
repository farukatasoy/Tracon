# Faz 125 — Üretilen Tool Şemasının İfade Gücü

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](../../kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **T-1**, **T-2**
> **Önkoşul:** [Faz 52](52-KAYNAK-URETECI.md) (kaynak üreteci ve derleme anı doğrulama) — arşivde; yalnız grep'le okunur
> **Paketler:** `Tracon.Generators`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Açıklama `System.ComponentModel.DescriptionAttribute`'tan okunur — Tracon yeni bir attribute **sevk etmez**
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/write-your-own-tool.md`, `troubleshooting.md` (TRC0003 bölümü) · sevk edilen: `AnalyzerReleases.Unshipped.md` (yeni APG kuralı), `[TraconTool]` XML dokümanı
> **Manuel test alanı:** `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 8bf0f3c:docs/arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md
> ```
>
> Damıtıldı 2026-09-01 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir tüketici ölçtü: kendi basit reflection şeması **parametre açıklaması üretiyor**, Tracon'in kaynak üreteci üretmiyor. Modelin hangi argümanı neyle dolduracağını en çok etkileyen alan budur ve bu eksende Tracon bugün geridedir. Aynı üreteç yolu, rehberde *"the AOT-safe path"* diye **önerilen** yoldur; önerdiğimiz yolun daha zayıf olması kabul edilemez.

## Bitiş Ölçütleri (DoD)

- [x] `[Description]` taşıyan bir parametre üretilen şemada `description` alanı taşır — anlık görüntü testi (`ToolSchemaDescriptionTests.A_Description_attribute_on_a_scalar_parameter_reaches_the_schema`)
- [x] Dizi parametresinde açıklama **dizi düğümünde**, `items` içinde değil (`An_array_parameters_description_is_written_on_the_array_node_not_inside_items`)
- [x] Açıklamasız parametre TRC0009 **uyarısı** üretir; derleme başarılıdır (`DiagnosticTests.TRC0009_warns_when_a_parameter_description_is_missing_but_does_not_block_generation`)
- [x] TRC0003 metni ifade edilemeyenleri adıyla sayar ve kaçış yolunu gösterir (`DiagnosticTests.TRC0003_message_names_what_the_generator_can_never_express_125_3`)
- [x] `guides/write-your-own-tool.md` üretecin sınırını ilan eder
- [x] Açıklama değişince üreteç yeni şema üretir (`Changing_only_the_description_produces_a_fresh_schema_not_a_stale_cached_one`)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`dotnet build`/`test`/`pack`/`format --verify-no-changes`, 2026-09-01)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı; modelin gördüğü şema çıktısı belgeye yazıldı (§125.1 "Gerçek çıktı")
- [x] `secret` taraması boş döndü (`python3 scripts/kapi.py tarama` → `✅ temiz`)
- [x] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi (MT-TEST-087/088/089)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (`check:content`+`build`+`check:links`+`check:weight`) temiz

### Doğrulama komutları

```bash
# Üretilen şemayı gerçekten gör
curl -s http://localhost:5080/tracon/api/tools -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool

# Üreteç testleri
dotnet build Tracon.slnx -c Release
```

---

## Plandan Sapmalar

- **`ToolRegistrationGenerator.cs` planın dosya listesinde yoktu, ama değişmek
  zorunda kaldı.** Plan `DescriptorsById` adlı bir dispatch tablosunun varlığını
  bilmiyordu: `ToolCandidate.Create` bir `DiagnosticInfo` üretse bile,
  `ToolRegistrationGenerator.ReportDiagnostic` id'yi bu **private** sözlükte
  bulamazsa sessizce `return` eder — ne derleme hatası, ne test kırılması, ne
  log çıkışı. TRC0009 önce `ToolDiagnostics.cs`'e eklendi ve **hiç
  raporlanmadı**; yalnız tanıyı bizzat arayan bir test (`ToolSchemaDescriptionTests`)
  bunu yakaladı. Aynı kusur sınıfının bir daha yaşanmaması için
  `DiagnosticIntegrityTests.Every_DiagnosticInfo_routed_tool_diagnostic_is_wired_into_the_generators_dispatch_table`
  eklendi — reflection ile private tabloyu okuyup her `Tracon.Tools` tanısının
  (doğrudan raporlanan `DuplicateName`/`NoToolsFound` hariç) orada olduğunu
  doğrular. Not: `docs/hafiza/analyzer-yazimi.md`.
- **`node.Substring(1)`, plandaki `node[1..]` değil.** `Tracon.Generators`
  `netstandard2.0`'ı hedefliyor; bu TFM'de `System.Range`/`System.Index` yok,
  dizin aralığı operatörü `CS0518` veriyor. Aynı sınıftan bir tuzak zaten
  `IsExternalInit` için biliniyordu (Faz 52); bu faz onu dizin aralığı
  operatörüne genişletti. Not: `docs/hafiza/analyzer-yazimi.md`.
- **Örnek/şablon/paket-testi dosyaları planda yoktu, dogfooding için değişti.**
  `samples/Tracon.Api/OrderTools.cs`, `samples/Tracon.Samples.CustomTool/OrderPreviewTools.cs`,
  `src/Tracon.Templates/content/Tracon.Starter/Tools/OrderTools.cs`,
  `tests/Tracon.Package.Tests/Infrastructure/ConsumerProject.cs` —
  TRC0009 devreye girince bu dosyalardaki parametreler uyarı üretmeye başladı;
  ana repo `TreatWarningsAsErrors=true` taşıdığından `samples/Tracon.Api`
  için bu gerçek bir **derleme hatasıydı**. Dördüne de `[Description]` eklendi;
  ayrıca beş mevcut üreteç testi (`GeneratedOutputTests`, `IncrementalityTests`)
  aynı sebeple güncellendi.
- **TRC0003 mesajı ve `troubleshooting.md`, `AIFunctionFactory.Create`'in
  `JsonSerializerOptions` alan overload'unu (kaynak-üretilmiş `JsonSerializerContext`
  ile AOT-güvenli) örnekliyor** — plan yalnız "register manually with
  `AddTool(AIFunctionFactory.Create(...))`" diyordu, hangi overload'ın nesne
  parametresini AOT-güvenli işlediğini söylemiyordu. Bir probe projesiyle
  ölçülüp (`AIFunctionFactory.Create(Delegate, string, string, JsonSerializerOptions)`)
  doğrulandı; `guides/write-your-own-tool.md` ve `troubleshooting.md`'ye
  çalışan bir örnek olarak yazıldı.

## Bu Fazda Verilen Kararlar

Bu fazda public API/uyumluluk sözleşmesi, güvenlik veya kiracı sınırı, kalıcı
veri/migration ya da geri dönüşü pahalı bir sistem kararı **verilmedi** —
yalnız yerel implementation tercihleri (yukarıdaki sapmalar). `docs/KARARLAR.md`'ye
yeni bir `K-*` kaydı açılmadı.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı `general-purpose` agent), taban `9621eeb`,
2026-09-01. Tam rapor bu oturumun geçmişinde; özet:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Hata-modu tablosu "çok uzun açıklama testte" diyordu ama böyle bir test yoktu | **Düzeltildi** — `A_very_long_description_round_trips_through_the_generated_schema` eklendi (209 → 210 test) |
| 2 | 🟢 | `troubleshooting.md` bu fazda büyüyünce `check:weight` tavanının **%96**'sına ulaştı | **Devredildi** — `docs/hafiza/dokumantasyon.md`'ye gözlem notu (kapı bugün yeşil, ölçülmeden büyütülmedi; yeni içerik eklerken kontrol edilmeli) |

🔴 bulgu **yok**. Denetçinin bağımsız doğruladığı: `dotnet build`/`test`
(Generators, 208/208), `dotnet format --verify-no-changes`, `build-agent-map.mjs --check`,
`npm run check`, `secret` taraması, imza-gövde takibi (`ParameterModel.Description`
üç üretim + iki tüketim noktası, kayma yok), `DescriptorsById` düzeltmesinin
kalıcılığı. Ayrıca doğrulandı: `Tracon.Ui.E2ETests`'teki tek düşen test
(`Playground_voice_mode_opens_microphone_and_shows_transcript`, paralel koşumda
30s timeout) bu fazın dokunmadığı dosyalarda — izole koşumda geçti, önceden var
olan kırılganlık, bu fazın kapsamı dışı.

## Sonraki Faza Devir Notu

- **Faz 126 (Kalıcı Payload Sürüm Sözleşmesi)** bu fazın dokunduğu hiçbir
  dosyaya bağımlı değil; bağımsız başlanabilir.
- **`[Description]` artık iki tool yazma yolunda da (kaynak üreteci VE
  `AIFunctionFactory.Create`) okunuyor** — ölçüldü (Açık Soru 1). Gelecekte
  ikisinden biri değişirse (MEAI sürüm yükseltmesi) `maf-api-kesfi` ile
  yeniden ölçülmeli; `docs/hafiza/analyzer-yazimi.md`'ye not düşülmedi çünkü
  bu bir üretici-tüketici sözleşmesi değil, MEAI'ın kendi davranışı.
- 🚨 **Yeni bir `APG*` tanısı eklerken `ToolDiagnostics.cs`'e eklemek
  YETMEZ.** `ToolRegistrationGenerator.cs`'deki private `DescriptorsById`
  sözlüğüne de eklenmeli, yoksa tanı sessizce hiç raporlanmaz —
  `DiagnosticIntegrityTests.Every_DiagnosticInfo_routed_tool_diagnostic_is_wired_into_the_generators_dispatch_table`
  bunu şimdi yakalıyor, ama tanının **kendisi** neden raporlanmadığını
  söylemez, yalnız "eksik" der. Kaçırma riski hâlâ var.
- **`troubleshooting.md` ağırlık bütçesinin %96'sında** (bkz. Denetim
  Bulguları #2). Bu sayfaya yeni bir bölüm eklemeden önce `npm run check:weight`
  çıktısını kontrol et.
- Faz 127 (Tool Kayıt Yüzeyi) `[Description]`'ın yaşadığı aynı üreteç
  boru hattına dokunacak (`ParameterModel`, `SourceWriter`) — bu fazda eklenen
  `Description` alanının artımlı önbellek eşitliğine **otomatik** girdiği
  (positional record) bilgisi hâlâ geçerli, ama yeni bir alan eklerken yine
  aynı önbellek testi deseni tekrarlanmalı (`Changing_only_the_description_produces_a_fresh_schema_not_a_stale_cached_one`).
