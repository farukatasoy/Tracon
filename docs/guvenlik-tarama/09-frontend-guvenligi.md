# Konu 9 — İstemci Tarafı (Frontend) Güvenliği

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`src/AgentPrism.UI/frontend/src` (React/TS), `docs-site/src` (Astro),
`EmbeddedUiProvider.cs` (gömülü varlık sunumu).

## Bilinen tasarım

Önceki tarama: `dangerouslySetInnerHTML`/`innerHTML`/`eval(` **0 sonuç**.

## Ara

- Bu 0 sonucun HÂLÂ doğru olduğunu (regresyon taraması) — yeni eklenmiş
  bir bileşen bu deseni geri getirmiş mi.
- CSP header'ı var mı, CORS ayarı ne kadar geniş (`AllowAnyOrigin` gibi
  aşırı izinli bir yapılandırma var mı).
- `EmbeddedUiProvider.cs`'nin gömülü varlık sunumunun path traversal'a açık
  olup olmadığını (`../` içeren bir istek dosya sistemi dışına çıkabilir mi).
