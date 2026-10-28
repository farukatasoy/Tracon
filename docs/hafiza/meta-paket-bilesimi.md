# Meta Paketin Bileşimi

> `Tracon` meta paketinin neyi taşıdığı, neyi taşımadığı ve bileşimi kilitleyen
> kapılar (Faz 256, K-1116). Paketleme mekaniği (`dotnet pack`, nuspec,
> `IncludeBuildOutput=false`) [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md)'dedir.
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnızca bu alana dokunurken okunur.
> `paketleme-ve-dagitim.md` Faz 256'da bütçesini aştı, bu bölüm ayrıldı.
> Faz 257'den beri şablonun ilk agent yolu da buradadır (son bölüm).

- **🚨 Meta paketin bileşimini değiştirirsen `Tracon.Package.Tests` projesinin TAMAMINI koş,
  üç sınıfı değil.** Faz 256'da graf, şablon ve çalıştırma sınıfları yeşildi; `ConsumerSurfaceTests`
  `MigrationRunner` vakası yalnız kapanışın tam testinde düştü. `MigrationRunner` depo paketlerinde
  `internal`'dır; meta paket depoyu getirmeyince tip grafta hiç yoktu ve derleme beklenen `CS0122`
  yerine `CS0246` verdi. Yapısal kural: bir tüketici yüzey sondası, daraltılmış tipin **sahibi
  paketi** açıkça referanslar (`SurfaceProbeProject.WriteAsync(…, owningPackage)`).
- Bileşimi üç kapı kilitler: `DependencyDirectionTests.The_meta_package_reaches_no_storage_package`
  (proje grafı), `transitive-dependencies.txt` `# Tracon` bölümü (paket grafı) ve
  `TemplateInstantiationTests` (şablon çıktısında `Npgsql/` anahtarı: `memory`/`sqlserver` yok,
  `postgres` var). Biri tek başına yetmez.
- Meta paketten depo isteyen repo içi proje (örnek, kapasite host'u) açık referans alır. Kapasite
  host'u kapanış kapısında **derlenmez**; `scripts/capacity.py` `prepare_consumer` yolunu elle koş.

## Şablonun ilk agent yolu (Faz 257)

- `dotnet new tracon-api --model <ad>` modeli `appsettings.json`'a yazar (`DefaultModel`; Azure `DefaultDeployment`).
  Şablon motoru **türetilmiş** bir sembol kullanır (`modelJson`, `valueTransform: jsonEncode`). 🚨 `jsonEncode`
  biçimi tırnakları **kendisi** ekler: belirteç `appsettings.json`'da tırnaksız yazılır
  (`"DefaultModel": TRACON_TEMPLATE_MODEL_NAME`), aksi hâlde değer `""m-1""` olur. Boş değer `""` üretir; `"` ve `\`
  geçerli JSON kalır (ölçüldü, `TemplateModelNameTests`).
- `Program.cs` modeli `builder.Configuration["Tracon:Providers:<P>:DefaultModel"]`'den okur ve `ModelBinding.Model`'e
  açıkça bağlar. Sağlayıcı bağlıyken ad boşsa açılış durur; sağlayıcı yoksa uygulama kurulumsuz açılır.
- Rehberin yolu bir kapıdır: `FirstAgentPathTests` `first-agent.md`'nin `## Create the host` … `## Run it` komutlarını
  çalıştırır, adım sayısı `StepBaseline`'dadır (iki yönde sabit; kısalırsa tabanı indir). `bash` dışı **başlıklı**
  blok (`title="…"`) "dosya düzenle" adımı sayılır: rehbere başlıklı bir `Program.cs` alıntısı koyma, başlıksız koy.
- 🚨 Ölçüm komutunda `grep -q Tracon.PostgreSql` **yanlış negatif** verir: `.` her karakterle eşleşir ve
  `Tracon:PostgreSql:ConnectionString` (yapılandırma anahtarı) paket adı sayılır. Plan dokuz sayfa buldu, kapı on bir
  buldu. `ShippedDocumentationConsistencyTests` eşlemeyi kaynaktan türetir (`public static ITraconBuilder Ad(this
  ITraconBuilder` + meta paketin `ProjectReference` kapanımı), elle yazılmış liste yoktur.
