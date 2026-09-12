# Konu 3 — Denetim İzi ve Secret Redaksiyonu

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`src/Tracon.Core/Audit/` (`AuditActorContext`, `AuditSecretFilter`,
`AuditRecorder`, `Auditing*Store`), `AsyncLocal` kullanım noktaları,
`record` tipli ayar/credential sınıfları, `SecretLeakTests`.

## Bilinen tasarım

`AuditSecretFilter` ALAN ADINA bakar (`apiKey`/`authorization`/`password`/
`secret`/`token` — K-081 tekil/çoğul ayrımı gerçek bir kusurdu, kapatıldı),
DEĞERE bakmaz. `record` tipli ayar sınıflarının derleyici-üretimli
`ToString()`'i TÜM alanları (secret dahil) sızdırabilir; `SecretLeakTests`
bunu tip taramasıyla zorluyor. K-059: secret asla veritabanına yazılmaz,
yalnız yapılandırma anahtarının ADI saklanır. `AuditActorContext` bir
`AsyncLocal`dır; async yardımcı metotta AÇILAMAZ (yazım çağırana geri
akmaz) — bu tuzak proje genelinde 5 kez yaşandı.

## Ara

- Bilinen alan-adı listesinde (`apiKey/authorization/password/secret/
  token`) OLMAYAN ama hassas olan yeni bir alan adı var mı
  (`connectionString`, `privateKey`, `sessionId`, `refreshToken`,
  `clientSecret` gibi varyasyonlar) — `AuditSecretFilter`'ın kaynak kodundaki
  tam listeyi oku, kod tabanındaki gerçek alan adlarıyla karşılaştır.
- Son eklenen her `record` ayar/credential tipinin `SecretLeakTests`
  kapsamına GERÇEKTEN girdiğini (yeni tip listeye unutulmamış mı).
- Herhangi bir async yardımcı metodun (`async Task` dönen, çağıran
  metottan ayrı) içinde `AuditActorContext`'in veya başka bir `AsyncLocal`ın
  set edildiği bir nokta var mı — `docs/hafiza/cekirdek-calistirma.md`'deki
  desene bak.
- Sandbox script çalıştırmadaki "zorunlu audit yazımı" kapısının (K-089)
  audit store hata verdiğinde GERÇEKTEN çalıştırmayı kestiğini, sessizce
  geçmediğini.
