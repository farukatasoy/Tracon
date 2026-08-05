# Build, Paketleme ve Analyzer Tuzaklari

> MSBuild, NuGet, AOT, .editorconfig, Meziantou/Roslyn tanilari.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **`Enum.TryParse<T>` / `Enum.IsDefined` / `Enum.GetNames<T>()` AOT temiz** (2026-08-02): `ReasoningEffort` çevrimi bunlarla yazıldı, hiçbir `IL2026`/`IL3050` çıkmadı.
- **AOT üç yerde ödün istedi** (2026-08-02): `ValidateDataAnnotations()` → elle validator; `optionsBuilder.Bind()` → elle bağlama; tool argümanı serileştirme → elle biçimlendirme. Faz 2'de `jsonb` için `JsonSerializerContext` gerekecek.
- **`Guid.CreateVersion7()` net9+** (2026-08-02): `net8.0` da hedeflediğimiz için `AgentPrismId.NewId()` yazıldı (RFC 9562). Birincil anahtarlarda `Guid.NewGuid()` **kullanma** — index parçalanır.
- **`dotnet pack` kodsuz uyarı üretir** (2026-08-01): `IsPackable=false` olan projeler için NuGet **kodsuz** bir uyarı verir; `NoWarn` ile susturulamaz. Çözüm `<WarnOnPackingNonPackableProject>false</WarnOnPackingNonPackableProject>`. Kaynak: `NuGet.Build.Tasks.Pack.targets` satır 204. `Directory.Build.props` içinde boş `<Target Name="Pack" />` tanımlamak **çalışmaz** — props SDK hedeflerinden önce yüklenir.
- **`CentralPackageTransitivePinningEnabled` kütüphanede zararlı** (2026-08-01): geçişli bağımlılıkları üretilen `.nuspec` içine **doğrudan** bağımlılık olarak yazar. Ölçüldü: `AgentPrism.PostgreSql` 13 → 2 doğrudan bağımlılık.
- **Trim/AOT analyzer'ları kök seviyede açılamaz** (2026-08-01): `app.MapGet(pattern, delegate)` `IL2026` + `IL3050` üretir. Analyzer'lar `src/` katmanında, paket bazlı kapatılabilir olmalı.
- **`.editorconfig` isimlendirme kurallarında sıra önemli** (2026-08-01): ilk eşleşen kural kazanır. `const` ve `static readonly` kuralları genel private alan kuralından **önce** gelmelidir.
- **`MA0004` `await using` ifadelerini de kapsar** (2026-08-02): kütüphane kodunda hata seviyesinde. Kalıp: `var x = ...;` sonra `await using (x.ConfigureAwait(false)) { ... }`. Doğrudan `await using var x = ....ConfigureAwait(false)` yazmak değişkenin tipini `ConfiguredAsyncDisposable` yapar ve kullanılamaz hale getirir.
- **Ham interpolasyonlu dizede `{{` kaçış değildir** (2026-08-02): tek `$` ile açılan ham dizede `{` her zaman interpolasyon başlatır; `'{}'::jsonb` yazmak CS9006 verir. Çözüm: sütunu INSERT listesinden çıkarıp şema varsayılanına bırak, ya da iki `$` ile aç.
- **`Convert.ToHexStringLower` net9+** (2026-08-02): `net8.0` da hedeflendiği için `Convert.ToHexString` kullanılır. Migration checksum'ları bu yüzden büyük harf onaltılıktır.
- **Statik sınıf tür argümanı olamaz** (2026-08-02): `AddToolsFrom<OrderTools>()` `CS0718` verir çünkü tool sınıfları genelde `static class`. Bu yüzden `AddToolsFrom(Type)` aşırı yüklemesi var.
- **`record` ayar sınıfı sır sızdırır** (2026-08-02): derleyicinin ürettiği `ToString` tüm özellikleri yazar. Ayar sınıfları `class` olmalı; `SecretLeakTests` bunu tip üzerinden denetler (`GetMethod("ToString").DeclaringType == typeof(object)`).
- **🚨 `IsAotCompatible` `src/Directory.Build.props` içinde türetilemez** (2026-08-02): o dosya csproj gövdesinden **önce** yüklenir; csproj'da yazan `AgentPrismAotCompatible=false` görülmez ve bayrak geri alınamaz biçimde `true` kalır. Türetme `Directory.Build.targets` içindedir. Aynı tuzak csproj'a bakan her türetilmiş özellik için geçerli.
- **🚨 MSBuild hedef `Condition`'ı `DependsOnTargets`'tan ÖNCE değerlendirilir** (2026-08-02): bağımlılık zinciri hedeflerin kendi üzerinde kurulursa (`A` → `B` → `C`) ve `B`'nin koşulu `C`'nin ürettiği bir özelliğe bakıyorsa, `C` **hiç çalışmaz**. Zinciri en dıştaki hedefin `DependsOnTargets` listesinde sırayla kur. Yaşandı: Node algılama hedefi hiç koşmadı, arayüz sessizce derlenmedi.
- **🚨 `Sdk="..."` niteliğiyle yüklenen SDK hedefleri projenin EN SONUNA gelir** (2026-08-02): csproj gövdesindeki `<Import Project="...targets" />` daha önce yüklenir ve içindeki `BeforeTargets="AssignTargetPaths"` *"does not exist in the project, and will be ignored"* diye **sessizce** atılır. SDK hedeflerine kanca atan bir `.targets` için açık `<Import Project="Sdk.props|Sdk.targets" Sdk="Microsoft.NET.Sdk" />` biçimini kullan. Karar K-051.
- **🚨 Çok hedefli projede iç derlemeler paralel koşar** (2026-08-02): tek bir çıktı dizinine yazan bir dış araç (Vite, `emptyOutDir`) üç kez aynı anda çalışır ve `ENOENT ... unlink` verir. Böyle adımlar `BeforeTargets="DispatchToInnerBuilds"` ile dış derlemeye alınır. Karar K-050.
- **`MA0009` kaynak üretilmiş `[GeneratedRegex]`'i de yakalar** (2026-08-02, Faz 8): "regex DoS" analizi timeout kontrolü sağlanamayan her regex'i işaretler; `GeneratedRegexAttribute`'ün timeout aşırı yüklemesi yoktur. Basit sabit desenler (ör. `^[a-z0-9][a-z0-9-]{0,31}$`) için regex'ten tamamen vazgeçip elle karakter döngüsü yazmak hem analyzer'ı susturur hem daha az koddur.
- **`System.Threading.Lock` net9+** (2026-08-02): `src/` net8.0 da hedefler; orada `lock` nesnesi olarak listenin kendisi kullanılır — ayrı bir `object` alanı `MA0158` tetikler. Test projeleri net10.0'dır ve `Lock` kullanabilir.

<!-- MEMORY.md'de ozeti var; tam metin burada korunur -->
- **`dotnet format`, `dotnet build`'den fazlasını yakalar** (2026-08-02): `EnableConfigurationBindingGenerator=true` ile build temiz geçti ama format `IL2026`/`IL3050` gösterdi — kaynak üreteci format'ın analyzer geçişinde devreye girmiyor. **Dört kapıyı da çalıştır**; sadece build'e güvenme.
- **🚨 Faz 11 `dotnet format` calistirmadan kapanmis** (2026-08-02, Faz 12): `main` uzerinde `dotnet build` 276 `IDE0055` hatasi veriyordu (uc bosluk girinti, `ISkillScriptGrantStore.cs` ve `SkillScriptGrant.cs` + turevleri). Faz kapanisinda dort kapinin da gercekten calistirildigini dogrulayin; `dotnet build` tek basina yesil gorunmuyordu bile.
- **Bash komutlarında `cd` kalıcıdır** (2026-08-01): bir komutta `cd artifacts/...` yapıldıysa sonraki komut oradan başlar. `rm -rf artifacts && dotnet build AgentPrism.slnx` sessizce yanlış dizinde çalıştı ve eski paketler doğru sanıldı. Doğrulama komutlarında mutlak yol kullan.

## AOT uyumluluk tablosunun gerekceleri

> `MIMARI.md` bolum 9'daki tablonun uzun gerekceleri buraya TASINDI (2026-08-05,
> Faz 30): sicak yol butcesi asilmisti ve bu satirlar ancak AOT'a dokunurken
> gerekir.

- **`AgentPrism.Core` — Evet.** Yansimaya dayanan tek yol `AddToolsFrom` ve
  `AddTool(Delegate)`; ikisi de `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`
  ile isaretlidir. Uyari **bastirilmaz**, cagirana iletilir.
- **`AgentPrism.SqlServer` — Hayir (vaat ertelendi).** Olculdu: sifir IL2/IL3
  uyarisi. Canli bir sorgu AOT altinda dogrulanmadigi icin vaat verilmedi (K-181).
- **`AgentPrism.Sqlite` — Hayir (olculmedi).** Faz 24 kapanisinda olcum YAPILMADI;
  `SQLitePCLRaw` yerel kutuphane tasir (K-196).
- **`AgentPrism.OpenAI` — Evet.** Faz 3'te olculdu: `IsAotCompatible=true` ile
  sifir uyari. `OPENAI001` / `MAAI001` **deneysel API** tanilaridir, AOT tanisi
  degildir — karistirilmamalidir.
- **`AgentPrism.AspNetCore` — Hayir.** Minimal API delege yonlendirmesi reflection
  kullanir. Bayrak `Directory.Build.targets` icinde turetilir: `src/Directory.Build.props`
  csproj'dan **once** yuklendigi icin orada turetmek tuketicinin `false` tercihini
  yok sayardi (K-006).
