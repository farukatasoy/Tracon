# Konu 8 — AOT/Reflection Disiplini

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`grep -rln "Reflection\|Activator.CreateInstance" src/` çıktısındaki
dosyalar (`ToolMethodScanner.cs`, `EmbeddedUiProvider.cs`,
`WorkflowRunner.cs`, JSON source-gen context dosyaları ve diğerleri).

## Bilinen tasarım

Her reflection kullanımı `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`
ile işaretli ve belgelenmiş bir AOT alternatifi sunuyor (ör.
`ToolMethodScanner` → `AddTool(AIFunction, bool)`).

## Ara

- Her reflection/`Activator.CreateInstance` çağrısında YÜKLENEN TİP ADININ
  sabit kodda mı yoksa dış girdiden (kullanıcı, config, HTTP body) mi
  geldiğini — girdiden geliyorsa bu kontrolsüz tip yükleme = potansiyel RCE.
- `TraconAotCompatible=false` işaretli projelerin (`grep -l
  "AotCompatible>false" src/*/*.csproj`) gerekçesinin hâlâ geçerli olduğunu.
