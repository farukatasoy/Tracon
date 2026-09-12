# Faz 37 — `dotnet new` Proje Şablonu

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-49**
> **Önkoşul:** Yok. Faz 33 (sağlık denetimi) önce biterse şablon onu da taşır
> **Paketler:** yeni — `Tracon.Templates`
> **Yeni paket:** **Evet** — gerekçe aşağıda · **Migration:** Yok
> **Public API:** büyümüyor — şablon paketi kod yüzeyi taşımaz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/37-PROJE-SABLONU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`samples/Tracon.Api` çalışan bir kontrol düzlemidir ama bir **şablon değildir**. Yeni bir kullanıcı onu kopyalamak, adları değiştirmek ve gereksiz parçaları silmek zorundadır. - **F-49** — `dotnet new tracon-api` → çalışan bir kontrol düzlemi. Bu, dalganın **ilk on dakikaya** dokunan tek kalemidir.

## Bitiş Ölçütleri (DoD)

- [x] `dotnet new install ./src/Tracon.Templates` başarılı — doğrulandı,
      `tracon-api` şablonu listelendi
- [x] `dotnet new tracon-api -n Benim.Agent` çalışır ve üretilen projede
      `Tracon.Starter` dizesi **hiçbir dosyada kalmaz** — `TemplateRenameTests` geçti
- [x] Varsayılan (`memory`) birleşim **hiçbir kurulum olmadan** `dotnet run`
      ile ayağa kalkar; `GET /tracon/api/agents` `200` döner — hem elle hem
      `TemplateRunTests` ile doğrulandı (`[{"name":"support",...}]` döndü)
- [x] `--persistence sqlserver --provider azure --ui true` (en dolu birleşim —
      postgres yerine SqlServer+Azure seçildi, ikisi de meta pakete dâhil
      DEĞİL, kırılmayı daha güçlü yakalar) sıfır uyarıyla derlenir —
      `TemplateInstantiationTests.EnDoluBirlesim_...` geçti
- [x] 🚨 Üretilen `appsettings.json` yalnız boş placeholder taşır; **üretilen
      projede** `secret` taraması boş döner — `TemplateSecretTests` (2 test) geçti
- [x] Üretilen kodda sabitlenmiş model adı yoktur (K-032) — `TemplateModelNameTests`
      dört sağlayıcının tamamı için geçti
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format`
      hepsi `Tracon.slnx` üzerinde 0 uyarı/0 hata ile geçti
- [x] `secret` taraması boş döndü (depo geneli — `faz-tamamlama` komutu) — boş
- [x] `README.md` kurulum bölümü şablon komutunu içerir
- [x] `Tracon.slnx` iki yeni projeyi taşır

### Doğrulama komutları

```bash
# Sablonu kur
dotnet new install ./src/Tracon.Templates

# En yalin birlesim
TMP=$(mktemp -d)
dotnet new tracon-api -n Benim.Agent -o "$TMP/yalin" --persistence memory --ui false
dotnet build "$TMP/yalin" -c Release

# Yeniden adlandirma tam mi — cikti BOS olmali
grep -rn "Tracon.Starter" "$TMP/yalin" && echo "ADLANDIRMA EKSIK" || echo "temiz"

# 🚨 secret taramasi — cikti BOS olmali
grep -rniE "sk-[a-z0-9]{20}|api[_-]?key\"\s*:\s*\"[^\"]+\"" "$TMP/yalin" \
  && echo "SECRET VAR" || echo "temiz"

# Calisiyor mu
(cd "$TMP/yalin" && dotnet run &) && sleep 8
curl -s -i http://localhost:5081/tracon/api/agents | head -1

# En dolu birlesim
dotnet new tracon-api -n Benim.Agent2 -o "$TMP/dolu" --persistence postgres --ui true
dotnet build "$TMP/dolu" -c Release
```

---

## Plandan Sapmalar

- **Sürüm sabitleme yerine kayan `TraconVersion=*-*`.** Açık Soru 2'nin
  önerisi ("A: aynı sürüm") bir **build-time token stamping** mekanizması
  ima ediyordu (şablon paketi paketlenirken `template.json`'daki placeholder'ı
  gerçek `$(PackageVersion)` ile değiştirmek). Uygulama sırasında bu, kaynak
  dosyayı `dotnet pack` çalıştıkça MUTASYONA uğratmadan yapmak için ayrı bir
  ara-kopyalama aşaması gerektirdiği görüldü — karmaşıklık/değer oranı düşük.
  Bunun yerine NuGet'in floating version söz dizimi (`*-*`) kullanıldı: her
  zaman yapılandırılan kaynaktaki en güncel ön-sürümü alır, `--TraconVersion`
  ile geçersiz kılınabilir. Bkz. K-265.
- **Örnek tool sayısı ikiden bire indi.** Plan "tek basit tool" diyordu (Açık
  Soru 3); ilk taslak `samples/Tracon.Api`'deki üç tool'un (`get_order_status`,
  `list_recent_orders`, `cancel_order`) hepsini kopyalıyordu. Kapanışta tek
  `get_order_status` bırakıldı — `RequiresApproval` gibi ek kavramlar sablonun
  amacını (hızlı başlangıç) aşıyordu; onay akışı README'de metin olarak anlatılır.
- **`dotnetcli.host.json` eklendi** — planda yoktu. `--persistence`/`--provider`/
  `--ui` CLI bayraklarının `dotnet new -h tracon-api` çıktısında düzgün
  görünmesi için gerekliydi; `TraconVersion` parametresi orada `isHidden`
  işaretlendi (kullanıcı akışında görünmesi gerekmiyor).
- **Beklenmeyen NuGet paketleme tuzakları (37.3'ün "şablon bayatlar" riskinin
  ötesinde).** Plan yalnız kütüphane değiştiğinde şablonun kırılmasını
  öngörüyordu; gerçekte paketin **kendi ilk paketlenmesi** üç ayrı, birbirinden
  bağımsız NuGet davranışına takıldı — hiçbiri dokümante değildi ve hiçbiri
  açık bir hata mesajı vermedi (hepsi aynı `NU5017` arkasına gizlendi). Bkz.
  K-262/K-263/K-264 ve `docs/hafiza/build-ve-analyzer.md`.
- **`OrderTools.cs`'e açık `using Tracon;` eklendi** — planda yoktu, plan
  `samples/Tracon.Api/OrderTools.cs`'i örnek aldığı için bu satırın
  gereksiz olduğunu varsaymıştı. Otomatik testler (`TemplateInstantiationTests`,
  `TemplateRunTests`) `-n` ile yeniden adlandırılan bir projede bunun
  **derlemeyi kırdığını** yakaladı. Bkz. K-266.

## Bu Fazda Verilen Kararlar

K-262 — K-266. Ayrıntı ve gerekçe: `docs/KARARLAR.md`.

| Karar | Özet |
|---|---|
| K-262 | Şablonda `IncludeSymbols=false` zorunlu — boş sembol paketi `NU5017` verir |
| K-263 | Şablonda `TargetFrameworks` (çoğul) boşaltılır — `dotnet pack` çapraz-hedeflemeyi önler |
| K-264 | Şablon içeriği `<None Pack="true" PackagePath="content/...">` ile paketlenir |
| K-265 | Şablon `Tracon` paket sürümü varsayılanı kayan `*-*`'dir |
| K-266 | Üretilen `OrderTools.cs` açık `using Tracon;` taşır (yeniden adlandırma güvenliği) |

## Sonraki Faza Devir Notu

- **🚨 Yeni bir `IncludeBuildOutput=false` paketi eklerken K-262/K-263'ü
  tekrar keşfetmeyin.** `src/Directory.Build.props` her pakete `IncludeSymbols=true`
  ve `TargetFrameworks=net8.0;net9.0;net10.0` dayatır; derlenmeyen bir paket
  (sembol yok) her ikisini de açıkça kapatmalıdır, aksi hâlde `dotnet pack`
  `NU5017` ile başarısız olur — hata mesajı `Tracon` paketinin (dolu
  `<files>` listesiyle) DEĞİL, boş sembol paketinin sorunu olduğunu SÖYLEMEZ.
  Ayrıntı: `docs/hafiza/build-ve-analyzer.md`.
- **🚨 `sourceName` ile yeniden adlandırılan bir ad alanında C#'ın kapsayan
  ad alanı kısayoluna güvenmeyin.** Bkz. K-266. Şablon içeriğine yeni bir
  `.cs` dosyası eklerken, `Tracon` namespace'indeki bir tipi (örn. yeni
  bir öznitelik) kullanıyorsa açık `using Tracon;` yazın.
  `samples/Tracon.Api`'deki dosyalar bu kısayola güvenebilir çünkü ORADA
  ad alanı asla yeniden adlandırılmaz — şablon içeriği farklıdır.
- **Karar defteri indeksi bölündü (K-214'ün sözü tutuldu).** `docs/KARARLAR-INDEKS.md`
  artık yalnız en yeni 150 kalıcı kararı taşır; daha eskisi
  `docs/arsiv/KARARLAR-INDEKS-ARSIV.md`'dedir (sıcak yol dışı). Yeni bir karar
  eklerken hiçbir ek adım gerekmez — `python3 scripts/dokuman-bakim.py`
  bölünmeyi kendiliğinden korur (`ARSIV_ESIK = 150`).
- **`samples/Tracon.Api`'ye dokunulmadı** — Açık Soru 1'in kararı (A:
  ayrı kalsın) korundu. Şablon ve örnek uygulama farklı amaçlar taşımaya
  devam ediyor.
- **Yarım kalan iş yok.** Tüm DoD kalemleri karşılandı (bkz. aşağıdaki tablo).
  Sıradaki faz kimliği bağımsızdır (`docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`).
