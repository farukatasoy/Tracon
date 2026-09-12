# Faz 143 — Tool Argümanının Sözleşme Testleri

> **Durum:** ✅ Tamamlandı (2026-09-04)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-189** (tüketici turu 3, B7)
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Testing.Contracts.Xunit`
> **Yeni paket:** Yok — mevcut sözleşme paketine ek (K-605 ile sevk edildi) · **Migration:** Yok
> **Public API:** Büyüyor — yeni `abstract` sözleşme sınıfları. Yalnız test paketinde; tüketicinin çalışma anı grafiğine **girmez**
> **Tüketici yüzeyi:** `docs-site/`: `guides/testing.md`, `guides/write-your-own-tool.md`, `packages.md` · sevk edilen: `src/Tracon.Testing.Contracts.Xunit/README.md`
> **Manuel test alanı:** [`docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md`](../../manuel-test/24-TEST-PAKETI-VE-SABLON.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 15f1f734:docs/arsiv/fazlar/143-TOOL-ARGUMANININ-SOZLESME-TESTLERI.md
> ```
>
> Damıtıldı 2026-09-04 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon iki yerde fail-closed davranış **vaat ediyor**: `throw` eden bir argüman doğrulayıcı çağrıyı reddeder, `throw` eden bir yetkilendirme handler'ı çağrıyı engeller. Bu vaatler Tracon'in **kendi** kodunda test ediliyor — ama **tüketicinin** implementasyonunda test edilmiyor. Tüketicinin ölçümü: yirmi iki tool'un yedisi yıkıcı.

## Bitiş Ölçütleri (DoD)

- [x] Suite doğru bir validator'da geçer, **kabul eden** bir validator'da **düşer** (case 1 + 2) — MT-TEST-090/091
- [x] Her `[Fact]` için kasten kırık karşı örnek yazıldı ve kırmızı olduğu görüldü — 11 self-proof testi (`ToolContractSelfProofTests`), 4 kasten kırık implementasyon
- [x] Aynı tohum aynı argümanları üretir; suite kırılgan değil — `SchemaArgumentGeneratorTests` determinizm testleri, MT-TEST-092
- [x] Desteklenmeyen şema **açıkça** atlanır ve sebebi çıktıya yazılır — MT-TEST-093
- [x] `AIFunction.JsonSchema` imzası `maf-api-kesfi` ile doğrulandı (tahmin edilmedi) — `AIFunctionDeclaration.JsonSchema : JsonElement` (dump-api.sh)
- [x] Test paketi tüketicinin çalışma anı grafiğine sızmaz (`DependencyDirectionTests`) — MT-TEST-094, izin listesi hâlâ yalnız `Tracon.Abstractions`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis` iki kez (denetim düzeltmesi öncesi/sonrası), ikisi de yeşil
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `kapi.py kapanis` içindeki `kapi.py tarama` adımı temiz
- [x] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi — MT-TEST-090..094
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1×🟡 (kapandı), 1×🟢 (adaya yazıldı)
- [x] `docs-site/` güncellendi; `npm run check` (dört kapı: içerik/derleme/bağlantı/ağırlık) temiz

---

## `samples/Tracon.Api` ile gerçek run kanıtı

Bu faz `samples/Tracon.Api`'nin çalışma anı davranışına dokunmaz (test-anı
paketidir) — bu adım fazın **regresyon üretmediğinin** kanıtıdır, yeni bir
davranışın değil.

```bash
curl -s -X POST http://localhost:5081/tracon/api/agents/support/run \
  -H "Authorization: Bearer manuel-test-token-2026" -H "Content-Type: application/json" \
  -d '{"message":"Where is my order ORD-7?"}'
```

Gerçek OpenAI çağrısı (`gpt-5.4-mini`), SSE akışı üzerinden: model
`get_order_status` tool'unu `{"orderId":"ORD-7"}` argümanıyla çağırdı, tool
`"Order ORD-7 has shipped. Estimated delivery: 2 days."` döndürdü, model bunu
özetleyen bir metinle bitirdi (`finishReason: stop`, 398 girdi + 20 çıktı
token). Varsayılan kurulumda hiçbir `IToolArgumentsValidator`/
`IToolAuthorizationHandler` kayıtlı değildir — akış hiç engellenmeden aktı,
tam olarak dokümante edilen "hiçbir şey kaydetmeyen kurulum bugünkü davranışı
aynen korur" vaadiyle tutarlı.

## Plandan Sapmalar

1. **`Tool`/`Validator`/`Handler` plain sync abstract property değil, `CreateXAsync()` async
   factory + `IAsyncLifetime`.** Planın taslağı `protected abstract AIFunction Tool { get; }`
   yazıyordu ama `IAsyncLifetime`'ı da bildiriyordu — kendi içinde tutarsızdı. Paketteki
   HER diğer sözleşme (`CustomToolContract`, `RunJudgeContract`, `ModelProviderContract`)
   `ValueTask<T> CreateXAsync()` + `InitializeAsync` deseni kullanıyor; o yerleşik
   konvansiyon tercih edildi.
2. **`ToolAuthorizationContract` planın 3 satırlık taslağından önemli ölçüde
   sapıyor: `DeniedRequest` VE `AllowedRequest` adında iki abstract üye eklendi.**
   Yetkilendirmenin `ToolArgumentValidationContract`'ın aksine şemadan türetilebilen
   bir "geçersiz istek" kavramı yok — yetkilendirme kararı tüketicinin kendi iş
   kuralıdır. Zemin gerçeği olmadan hiçbir `[Fact]` anlamlı bir karşı örnekle
   kırmızıya düşürülemezdi (`CustomToolContract.ExpectedResultText`'in aynı deseni).
   `AllowedRequest` **denetimde eklendi** (aşağıya bak) — ilk taslak yalnız
   `DeniedRequest` taşıyordu ve `Handler_receives_the_calling_tenant`
   tenant'ı sessizce yok sayan bir handler'ı yakalayamıyordu.
3. **`A_pre_cancelled_token_is_honored` — planın 6 `[Fact]`'lik taslağında yok, Hata
   Modları tablosunun "İptal edilen doğrulama" satırında var.** Tablo plandan daha
   yetkili kabul edildi; `RunJudgeContract`'ın aynı adı taşıyan testiyle aynı desen.
4. **`tests/Tracon.Testing.Contracts.Tests/` değil `tests/Tracon.Testing.Contracts.Xunit.UnitTests/`.**
   Plan proje adını tahmin ediyordu. Gerçek konvansiyon `src/Directory.Build.props`'taki
   `InternalsVisibleTo Include="$(MSBuildProjectName).UnitTests"` — `SchemaArgumentGenerator`
   kasıtlı `internal` olduğu için (fuzzing altyapısı, sevk edilen genişleme noktası değil)
   bu adı taşıyan bir proje **zorunluydu**, plandaki ad çalışmazdı.
5. **`ContractSelfProofTests` `tests/Tracon.Testing.Contracts.Tests/`e değil,
   `tests/Tracon.Core.UnitTests/Tools/`e (`ToolContractSelfProofTests` adıyla)
   kondu.** Paketteki her diğer sözleşmenin (`CustomToolContractTests`,
   `DependencyDirectionTests`, `ToolContractCoverageTests`) dogfood'landığı **tek**
   yer orası; ayrı bir proje açmak aynı deseni ikiye bölerdi.
6. **`xunit.v3.assert` yeni bir paket referansı olarak eklendi (planda yoktu).**
   `xunit.v3.extensibility.core` `[Fact]`/`IAsyncLifetime` taşır ama `Assert` sınıfını
   (dolayısıyla `Assert.SkipWhen`) taşımaz — K-615 sınırını **açıkça** atlamak için
   dinamik skip zorunluydu. Paket yalnız assertion kütüphanesidir, `<OutputType>Exe</OutputType>`
   zorlamaz (nuspec'inde `buildTransitive` yok, ölçüldü).
7. **`SchemaArgumentGenerator` `JsonSerializer.SerializeToElement` yerine elle
   `Utf8JsonWriter` + `JsonDocument.Parse` kullanır.** İlkinin tek argümanlı
   (primitif) overload'ı bile reflection tabanlı üye çözümlemesi gerektirir ve
   IL2026/IL3050 uyarısı verir — paket AOT-uyumlu listede.
8. **🚨 Ölçülen kusur: MEAI 10.9.0'da nullable bir C# parametre (`string?`)
   `"type":"string"` değil `"type":["string","null"]` üretir.** Generator'ın ilk
   hâli yalnız `ValueKind == String` dalını okuyordu ve nullable HER parametreyi
   sessizce "desteklenmiyor" sayıyordu — plan probu 9.9.1 ile yazılmıştı, gerçek
   pakette (10.9.0) davranış farklıydı. `PrimaryType` yardımcı metodu (dizi formunu
   da okur) ile düzeltildi; `docs/hafiza/tool-onay-ve-yetkilendirme.md`'ye yazıldı.
9. **`Unknown_extra_property_is_handled_deliberately` — Açık Soru 1'in A seçeneği,
   `protected virtual bool ExtraPropertyIsRejected => true` ile uygulandı.** Suite karar
   dayatmıyor (tüketici override edebilir), ama sessiz bir no-op da değil — validator'ın
   davranışı beyan edilen değerle **eşleşmezse** kırmızı olur.

## Bu Fazda Verilen Kararlar

Hiçbiri `docs/KARARLAR.md`'ye girmedi — hepsi bu fazın kendi kapsamındaki yerel
implementation tercihi (test paketinin iç tasarımı); public API/uyumluluk
sözleşmesi, kiracı/güvenlik sınırı veya kalıcı veri kararı **değil**. Plandaki
"Açık Sorular" tablosunun üç maddesi de plan zaten seçenek A'yı önermişti; bu
faz onu doğruladı, yeniden tartışmadı.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent, `dotnet build` + hedefli `dotnet test`
ile ampirik doğrulama) **0×🔴** buldu.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `Handler_receives_the_calling_tenant`'ın adı/XML doc'u "TenantId gerçekten veri olarak okunuyor" iddia ediyordu ama assertion yalnız `ShouldNotBeNull()` kontrol ediyordu — TenantId'yi tamamen yok sayıp sabit bir karar dönen bir handler bu testi sessizce geçerdi. Kendi self-proof'u da bu boşluğu görmüyordu (`TenantLockedHandlerFixture` yalnız İKİNCİ tenant'ta throw eden bozuk implementasyonu yakalıyordu, sessizce yok sayanı değil). | **Düzeltildi.** `AllowedRequest` abstract üyesi eklendi; fact artık `DeniedRequest`/`AllowedRequest`'in **zıt** kararlar ürettiğini kanıtlıyor. Yeni self-proof karşı örneği: `AlwaysDenyToolAuthorizationHandler` (her zaman reddeder — `AllowedRequest`'i de yanlışlıkla reddederek yakalanır). |
| 2 | 🟢 | `SchemaArgumentGenerator.MismatchedValue`'nin `_ => IntegerElement(12345)` varsayılan kolu (bir `enum` property için `PrimaryType` `null` döndüğünde) hiçbir testte tetiklenmiyor. | `docs/ADAYLAR.md`'ye yazılmadı — kapsam dışı köşe durumu, davranış makul ama kanıtsız; küçük ölçekli bir F-NN açmaya değecek boyutta değil, not olarak burada bırakıldı. |

Düzeltme sonrası dört doğrulama kapısı **yeniden** koşuldu (bkz. DoD).

## Sonraki Faza Devir Notu

- **Devraldığı sözleşmeler:** `ToolArgumentValidationContract` (7 `[Fact]`) ve
  `ToolAuthorizationContract` (3 `[Fact]`), `Tracon.Testing.Contracts.Tools`
  ad alanında, `ContractCoverage.ToolContracts` kapsamında. İkisi de MAF/Tracon
  çalışma anı grafiğine **girmez** — yalnız test paketinde.
- **Davranış sözleşmeleri:**
  - `IToolArgumentsValidator`/`IToolAuthorizationHandler` fail-closed vaadi artık
    yalnız Tracon'in kendi wrapper'ında değil, **tüketicinin implementasyonunda**
    da sınanabilir bir hâle geldi.
  - `SchemaArgumentGenerator` (internal) yalnız düz `string`/`integer`/`number`/
    `boolean`/`enum` şema özelliklerini modelliyor; nested object/array **her
    zaman** açık bir `SkipReason` ile atlanır — K-615/K-655 sınırıyla tutarlı.
- **🚨 Bilinen tuzak (sonraki faz bu alana dokunursa):** `AIFunction.JsonSchema`'da
  `"type"` bir DİZİ olabilir (`["string","null"]`) — bkz. Plandan Sapmalar #8 ve
  `docs/hafiza/tool-onay-ve-yetkilendirme.md`. Şema TÜKETEN (üreten değil) yeni bir
  kod yazarken bu köşe durumunu unutma.
- **🚨 İkinci tuzak:** xunit.v3 yalnız `public` sınıfları test olarak keşfeder;
  `private`/`internal` bir sözleşme türevi normal koşuma hiç karışmaz. Bu, "sözleşme
  suite'i kasten kırık implementasyonda KIRMIZI olmalı" desenini (self-proof) YAZMANIN
  standart yoludur — tekrar kullan.
- **Yer tutucu / açık uç:** Yok. Plan'ın altı açık sorusunun üçü de (A/A/A) uygulandı;
  yedinci `[Fact]` (cancellation) ve `AllowedRequest` (denetim) planın **üstüne** eklendi,
  planın **altında** kalan bir madde yok.
