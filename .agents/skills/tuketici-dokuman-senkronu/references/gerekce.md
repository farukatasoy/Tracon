# Tüketici Doküman Senkronu — Gerekçe ve Vaka Kaydı

> `SKILL.md`'den bağlanan ayrıntı. Bu dosya yalnız **kapı kazanmış** tuzakların
> anlatısını taşır (Faz 91 devir notu).

## `dotnet test --filter` yazma — MTP sessizce yutar

Ölçüldü: `dotnet test … --filter CapabilityExampleTests` paketin **1004
testinin tamamını** koşar ve yeşil döner; daralttığını sanırsın. MTP'de
`--filter` diye bir seçenek yoktur, `--filter-class` / `--filter-method` /
`--filter-namespace` vardır ve yalnız **derlenmiş test ikilisi** doğrudan
çağrılırken geçerlidir.

Bayat komut üç arşiv fazında (`docs/73`, `74`, `75`) da durmuştu; Faz 92
`python3 scripts/kapi.py test --proje <Proje> --sinif "*Ad*"` ile düzeltti —
bu artık tek doğru biçimdir.
