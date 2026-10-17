# Ek — Yeni paket eklendiyse

> `faz-tamamlama` Adım 1'den açılır. Tetik: faz yeni bir `src/<Paket>/`
> projesi ekledi.

`dotnet pack` çıktısını say: paket sayısı beklenenle uyuşmalıdır. Yeni paket
ayrıca şunları ister — atlanırsa build veya test kırar:

- `src/<Paket>/README.md` (NuGet sayfasında görünür; `DependencyDirectionTests` zorlar)
- `Tracon.slnx` içine `<Project Path=... />`
- Meta pakete (`src/Tracon/Tracon.csproj`) `ProjectReference`
- `DependencyDirectionTests.AllowedReferences` içine bir satır
- Library paketiyse csproj'a `<TraconPackageFirstRelease>true</TraconPackageFirstRelease>`:
  nuget.org'da tabanı yoktur ve yayın provası onu restore edemez. **İlk yayından
  sonra kaldır** — paket son `v*` etiketinde varsa `kapi.py yayin` bayrağı bayat
  sayar ve kırmızı verir (Faz 187)
