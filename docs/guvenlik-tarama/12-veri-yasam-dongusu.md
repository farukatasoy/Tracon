# Konu 12 — Veri Yaşam Döngüsü

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

Konu 02 kiracı süzgecinin SORGUDA olup olmadığına bakar. Bu konu tek bir
kaydı **türev kopyaları ve durum geçişleri** boyunca izler: saklama, silme,
dışa aktarım, yeniden oynatma, dallandırma. Tehdit modelinin **A6**
(operatör) ve **A7** (doğrudan depo okuması) profillerini karşılar
([`MIMARI-TEHDIT-MODELI.md`](../MIMARI-TEHDIT-MODELI.md) § 2).

## Kapsam

`src/Tracon.Core/Retention/`, `src/Tracon.Core/Privacy/`,
`src/Tracon.Core/Replay/`, `src/Tracon.Core/Attachments/`,
`src/Tracon.Core/Sessions/ConversationBranchService.cs`,
`src/Tracon.Sql.Shared/Stores/` (`SqlRetentionStore.cs`,
`SqlRetentionPolicyStore.cs`, `SqlDataSubjectStore.cs`,
`SqlAttachmentStore.cs`, `SqlIdempotencyStore.cs`,
`SqlConversationBranchStore.cs`, `SqlWorkflowCheckpointStore.cs`,
`SqlTraceStore.cs`), `src/Tracon.AspNetCore/Endpoints/`
(`DataSubjectEndpoints.cs`, `RetentionEndpoints.cs`,
`AttachmentEndpoints.cs`).

## Bilinen tasarım

**Çerçeve kuralı:** saklama tercihi tek başına zafiyet DEĞİLDİR. Bulgu için
açık bir erişim sınırı ya da silme/iptal sözü, ve onu geçen somut bir okuyucu
veya sonraki işlem gerekir.

Saklama veri düzlemi kiracıya kilitlidir; `IRetentionStore`'un dört metodu
`tenantId` alır (K-279). SQL tek tabloyla üretilir (K-198), parti silme her
sağlayıcıda farklı tekniktir (K-200), zamanlama Faz 17 kuyruğunu yeniden
kullanır (K-202). Korelasyonlar BARE değil TAM NİTELENDİRİLMİŞ ad kullanır
(K-259). `MaxRows` sıra adımından çıkarılarak uygulanır (K-258).

Bilinen sınırlar — bunlar kusur değil **karar**: `audit_log` veri konusu
silmesinin dışındadır (K-456); `DocumentEmbeddings` hem silmenin hem dışa
aktarımın dışındadır (K-458). Önizleme ve silme AYNI SQL işlemi üzerinden
yürür: önizleme her zaman `ROLLBACK`, gerçek silme yalnız çağıranın denetim
yazımı başarılıysa `COMMIT` (K-462, K-370 emsali).

Sahiplik BİR KEZ atanır; sonraki her yazımda kaynaktan taşınır ve üç SQL
`store` ile bellek içi `store` sütunu `COALESCE` eder — "set → unset" meşru
bir geçiş DEĞİLDİR (K-689). Açık bir `AmbientRunAttributionScope` yalnız
SAHİPLİK için kayıtlı bağlamı ezer (K-692). Oturumsuz yazılan ek, saklama
tarafından silinir (K-217).

Sürdürme ve yeniden oynatma ÜÇ AYRI işlemdir, karıştırılmaz: `Replay`
(K-315), `ApprovalResume` (K-368), `RunContinuation` (K-583).

Ret kodları: reddedilen TEKİL kaynak `404` döner ve gövdesi var olmayan
kaynakla birebir aynıdır, reddedilen LİSTE `403` döner (K-684);
`POST /api/attachments` reddi `403`'tür (K-686).

**Geçmiş vaka:** K-399'da dört saklama hedefi `ForTarget`'ta `_ => null`
dalına düşüp yapılandırma varsayılanını SESSİZCE yok sayıyordu
(HATA-S1-005). Bu bir sınıftır, tek vaka değil.

## Ara

- K-399 sınıfı: `RetentionPolicyResolver.ForTarget` ve
  `TraconRetentionOptions` bugün HER `RetentionTargets` üyesini karşılıyor
  mu? 2026-08-20'den sonra eklenen her yeni tablo veya hedef saklama
  kapsamına girdi mi, yoksa sessizce süresiz mi saklanıyor?
- Veri konusu silmesinin (`SqlDataSubjectStore`) kapsadığı tablo kümesi ile
  kiracı içeriği tutan tablo kümesini KARŞILAŞTIR. K-456 ve K-458 dışındaki
  her fark ya bir eksiktir ya da yazılmamış bir karardır.
- K-462'nin sırası bugün de geçerli mi — hata yolunda `ROLLBACK` gerçekten
  çalışıyor mu, yoksa kısmi silme bırakan bir yol var mı?
- Silinen bir oturumun türev kopyalarını izle: `run_events`, ekler,
  `conversation_branches`, `workflow_checkpoints`, `idempotency_keys`,
  izler, puanlar. Biri silmeden SONRA hâlâ okunabiliyorsa, bu silme sözünün
  ihlalidir.
- `ConversationBranchService` bir dalı kopyalarken kaynak oturumun kiracısını
  ve sahipliğini taşıyor mu — K-689'un "set → unset" yasağı dal yolunda da
  tutuyor mu?
- `RunReplayService` ve `RecordedToolPlayback` kayıtlı bir `tool` sonucunu
  yeniden oynatırken o kaydın kiracısını doğruluyor mu; `ReplayMismatchGuard`
  atlatılabilir mi?
- Ek dosya yolunda K-217'nin kuralının hâlâ geçerli olduğunu doğrula.
  Oturumsuz bir ekin silinmeden önce bir URI ile okunabildiği bir pencere
  var mı?
- **A7 merceği:** bu konudaki hangi koruma yalnız uygulama katmanındadır ve
  doğrudan SQL erişimi olan biri için hiç çalışmaz? Listele — bu, tehdit
  modeli § 4.7'nin somut karşılığıdır.
