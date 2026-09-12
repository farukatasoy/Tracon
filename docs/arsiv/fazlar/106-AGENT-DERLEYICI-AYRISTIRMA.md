# Faz 106 — Agent Derleyici Ayrıştırma

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`arsiv/kesif/2026-08-23-yapisal-sorun-envanteri.md`](../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 105](105-DI-BILESEN-KOKU-AYRISTIRMA.md) — teknik zorunluluk yoktur; yapısal tur sırası composition root'tan compiler'a ilerler
> **Paketler:** `Tracon.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `AgentDefinitionCompiler` imzaları ve davranışı değişmez; `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza ve XML metni değişmez; üretilen API reference aynı kalır
> **Manuel test alanı:** [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 806965a:docs/arsiv/fazlar/106-AGENT-DERLEYICI-AYRISTIRMA.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`AgentDefinitionCompiler`, public compile overload'larını, dependency resolution'ı, chat options üretimini, compaction'ı, memory provider'larını, tool çözümlemeyi ve harness kurulumunu tek 1.617 satırlık sınıfta taşır. Faz sınıfı yeni abstraction ile sarmalamaz. Aynı sınıfı sorumluluk odaklı `partial` dosyalara böler.

## Bitiş Ölçütleri (DoD)

- [x] Ana compiler dosyası public orchestration sınırında kalır; compaction, memory, skills ve agent üretimi ayrı sorumluluk dosyalarındadır
- [x] Public compile overload'ları ve XML dokümanları birebir kalır
- [x] Compile-path matrisi sync, async, cached ve parameterized yolları kapsar
- [x] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili aileye eklendi ve otomatik olanlar koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Plandan Sapmalar

Plana birebir uyuldu. Planın önerdiği yedi dosyalık bölünme (106.2) aynen
uygulandı; tek ek, 106.3'ün istediği `AgentDefinitionCompilerPathTests.cs`
dosyasının somut içeriğiydi (plan dosya adını veriyordu, hücreleri vermiyordu).
Okuma sırasında mevcut testler bir matris olarak okundu ve gerçekten boş kalan
beş hücre bulundu:

- sync tam overload'ın (`Compile(definition, callableAgents, toolTransform,
  culture)`) `culture` parametresini gerçekten çözdüğünü kanıtlayan test yoktu
- aynısı async `CompileAsync` overload'ı için de eksikti
- sync/async yolların `ResolvedCallableAgents`'ı gerçekten
  `BackgroundAgentsProvider`'a bağladığını kanıtlayan test yoktu — yalnız
  `CompileCachedAsyncTests` cache fingerprint'ini test ediyordu, wiring'i değil
- `CompileParameterizedAsync` için culture+parametre birleşimi, shared
  instructions, callable agents ve BYOK-red hücrelerinin hiçbiri doğrudan
  test edilmiyordu (yalnız `EvalJobHandlerTests` üzerinden dolaylı kullanım
  vardı)

Bu beş hücre için sekiz test eklendi; hiçbiri yeni ürün davranışı eklemedi,
yalnız taşıma sonrası gövde kaymasını yakalayacak kanıt üretti (106.3'ün amacı
buydu).

## Bu Fazda Verilen Kararlar

Yok. Faz saf bir kod taşıma işiydi; public API/compatibility contract,
güvenlik/kiracı sınırı veya kalıcı veri kararı gerektiren bir seçim yapılmadı.

## Denetim Bulguları

`faz-denetim` skill'i, taze bağlamlı bir `general-purpose` agent ile koştu.
Yöntem: orijinal dosya `git show 8c99924:...AgentDefinitionCompiler.cs` ile
çıkarılıp yedi yeni dosyayla satır satır karşılaştırıldı (gövde kayması
taraması), `#pragma warning disable/restore MAAI001` sınırları her dosyada tek
tek doğrulandı, ve yeni test dosyasının 106.3 matrisini gerçekten kapattığı
kontrol edildi.

**🔴 yok. 🟡 yok.**

**🟢 (aday listesine değil, aynı oturumda düzeltildi):** Yeni test dosyasının
sınıf-üstü XML dokümanı, sync/async shorthand overload'larda culture
kapsamının `AgentDefinitionCompilerTests`'te olduğunu iddia ediyordu; gerçekte
culture çözümü `InstructionCultureResolutionTests`'te (resolver'ı doğrudan
test eder) yaşıyordu. Fonksiyonel etkisi yoktu, yalnız yanıltıcı bir
`<see cref>` referansıydı — denetim raporu geldiğinde hemen düzeltildi
(`AgentDefinitionCompilerPathTests.cs`'in sınıf dokümanı).

## Sonraki Faza Devir Notu

- Bu fazın taşıma deseni (bitişik metot dilimlerini sorumluluk eksenine göre
  yeni `partial` dosyalara taşımak, `#pragma warning disable/restore MAAI001`
  sınırlarını her dosyada bağımsız yeniden çizmek) Faz 107 ve 108 için de
  aynen geçerlidir — ikisi de aynı `AgentDefinitionCompiler` dosya ailesine
  komşu, benzer büyüklükte tek sınıfları (`RunRecordingAgent`,
  bellek-içi `RunStore`) ayrıştırıyor.
- `AgentDefinitionCompilerPathTests.cs`, `AgentDefinitionCompilerTests.cs`,
  `SharedInstructionsTests.cs` ve `CompileCachedAsyncTests.cs` dört ayrı
  dosyaya yayılmış durumda; sınıf-üstü XML dokümanları birbirine çapraz
  referans verir. Yeni bir compile-path testi eklerken önce bu dördünü
  tarayıp doğru dosyaya ekle — yeni bir beşinci dosya açmak matrisi
  parçalar.
- MAAI001 suppression'ı dosya bazında yeniden çizmenin güvenilir yolu:
  önce **hiç** pragma eklemeden taşı, sonra `dotnet build` çalıştır — eksik
  suppression `MAAI001` hatası, fazla suppression `IDE0079` hatası olarak
  geri döner (`TreatWarningsAsErrors=true` ikisini de yakalar). Bu fazda
  yedi dosyanın ikisi (`ChatOptions.cs`, `Dependencies.cs`) hiç suppression
  gerektirmedi; tahminle eklemek yerine derleyiciye sorulması hızlı ve
  kesin sonuç verdi.
