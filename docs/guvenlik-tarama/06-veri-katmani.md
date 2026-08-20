# Konu 6 — Veri Katmanı: SQL Enjeksiyonu ve At-Rest Koruma

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`src/AgentPrism.Sql.Shared/Stores/*.cs`,
`src/AgentPrism.PostgreSql|SqlServer|Sqlite/`.

## Bilinen tasarım

Ham ADO.NET (Npgsql, Microsoft.Data.SqlClient, Microsoft.Data.Sqlite)
kullanılıyor, EF Core yok. Parametrize sorgu disiplini elle uygulanıyor.
`conversation_items` ve `attachments.content` açık metin saklanıyor
(F-41, `docs/ADAYLAR.md` — bilinçli AÇIK aday, `IContentProtector` genişleme
noktası var ama varsayılan uygulama yok).

## Ara

- Her store dosyasında SQL metninin string concatenation/interpolation ile
  KURULMADIĞINI (yalnız parametre bağlamayla kurulduğunu) tara — özellikle
  dinamik `ORDER BY`/`WHERE` kolon adı alan sorgu builder'ları risklidir.
- K-464 (SQL sütun tam nitelemesi eksikliği, önceki denetimde bulundu)
  sınıfının başka bir sorguda tekrarlanıp tekrarlanmadığını.
- F-41'in durumunu doğrula — `conversation_items`/`attachments.content`
  hâlâ açık metin mi, README/dokümantasyonda bu sınır AÇIKÇA belirtilmiş mi
  (belirtilmemişse bu bir dokümantasyon boşluğu, F-41'i yeniden tasarlama).
