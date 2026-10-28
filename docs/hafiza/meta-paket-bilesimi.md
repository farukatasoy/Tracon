# Meta Paketin Bileşimi

> `Tracon` meta paketinin neyi taşıdığı, neyi taşımadığı ve bileşimi kilitleyen
> kapılar (Faz 256, K-1116). Paketleme mekaniği (`dotnet pack`, nuspec,
> `IncludeBuildOutput=false`) [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md)'dedir.
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnızca bu alana dokunurken okunur.
> `paketleme-ve-dagitim.md` Faz 256'da bütçesini aştı, bu bölüm ayrıldı.

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
