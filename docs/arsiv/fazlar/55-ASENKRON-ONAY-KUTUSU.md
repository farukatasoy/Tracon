# Faz 55 — Asenkron Onay Kutusu

> **Durum:** ✅ Tamamlandı (2026-08-09)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-69**
> **Önkoşul:** [Faz 46](46-DAYANIKLI-CALISTIRMA.md) — bu kalemi kolaylıktan **eksiğe** çeviren faz · [Faz 9](09-YONETISIM-VE-DENETIM-IZI.md) — rol politikaları
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.Sql.Shared`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** gerekli — `pending_approvals` tablosu, numara uygulama anında alınır
> **Public API:** büyüyor — yeni tipler, bir depo arayüzü, üç uç. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/55-ASENKRON-ONAY-KUTUSU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tool onayı bugün yalnız **aynı istemcinin bir sonraki turunda** verilebilir. Uç, gövdede `approvals` alanını bekler ve kararı `ToolApprovalResolver` çözer. Bekleyen onaylar için **depo yoktur**; `GET /api/approvals/pending` **yoktur**.

## Plandan Sapmalar

1. **Faza başlamadan önce ayrı bir çöküş düzeltildi (K-367).** Kullanıcı isteği
   fazı `MapAgentPrismMcpServer()`'ın tamamen boş bir veritabanında (migration'lar
   koşmadan önce) çökmesini önce çözüp sonra bu fazı geliştirmeyi istedi.
   Düzeltme bu fazın kapsamı değildir (Faz 50'nin MCP/A2A yüzeyine ait bir
   hatadır) ama aynı oturumda yapıldığı ve `IEndpointFilter` deseni onay
   denetimini ilgilendirdiği için kararı burada da kayıtlıdır.
2. **Açık Soru 1'in önerisi (B: senkron yol da yazsın) TERSİNE çevrildi
   (K-372).** Yalnız kuyruk yolu `pending_approvals`'a yazar. Gerekçe: senkron
   yolda zaten canlı bir istemci vardır; iki kanal aynı oturuma yarışan karar
   yazabilirdi.
3. **`IPendingApprovalStore.ExpireAsync` planın `ValueTask<int>` taslağı yerine
   `ValueTask<IReadOnlyList<PendingApproval>>` döner (K-371).** Süre sonu
   servisinin kapanan çalıştırmaların `RunId`'lerine ihtiyacı vardı.
4. **Testler planın önerdiği 6 ayrı sınıf yerine 2 dosyada toplandı.**
   `AsyncApprovalFlowTests`/`ApprovalAuthorizationTests`/`ApprovalDoubleDecideTests`
   → `ApprovalEndpointTests` (4 `[Fact]`); `ApprovalExpirationTests`/
   `ApprovalAuditTests` → `PendingApprovalStoreContract` + `ApprovalEndpointTests`
   içine gömüldü. Küçük, iyi isimlendirilmiş testler ek sınıf açmaktan daha
   ucuzdu; kapsam plandakiyle birebir aynı.
5. **Arayüz bundle payı ölçüldü, tahmin edilmedi.** Toplam JS payı **162,6 KB
   gzip / 250 KB bütçe** (87,4 KB kalan) — plan bölümündeki "tahminî 3–5 KB"
   yalnız bu ekranın payı değil, o an ölçülmemiş toplam paydı; gerçek toplam
   `docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`'deki Faz 53 öncesi ölçümle (151,3 KB)
   karşılaştırılabilir bir sonraki fazda güncellenmelidir.

## Bu Fazda Verilen Kararlar

- **K-367** — MCP/A2A onay-yüzeyi denetimi `Map*()` senkron kontrolden istek-bazlı `IEndpointFilter`e taşındı (kullanıcı kararı)
- **K-368** — `RunStatus.AwaitingApproval` + karar sonrası YENİ `RunId` (kullanıcı kararı, `AwaitingInput`/K-014 ile aynı ilke)
- **K-369** — `pending_approvals.run_id` FK CASCADE + sözleşme testlerine `PrepareRunAsync` kancası
- **K-370** — `DecideAsync` denetim izini karardan ÖNCE, `IAuditLog.WriteAsync`'i DOĞRUDAN çağırarak yazar (K-089 deseni)
- **K-371** — `ExpireAsync` imzası `IReadOnlyList<PendingApproval>` döner (plandan sapma)
- **K-372** — Senkron/MCP/A2A yolu `pending_approvals`'a hiç yazmaz, yalnız kuyruk yolu yazar (plandan sapma)

Tam gerekçeler: `docs/KARARLAR.md`, K-367–K-372.

## Testler ve doğrulama kapıları (kanıt)

- `dotnet build AgentPrism.slnx -c Release` → **0 uyarı, 0 hata**
- `dotnet test AgentPrism.slnx -c Release --no-build` → **3057/3057 geçti**
  (`AgentPrism.SqlServer.IntegrationTests`in 471 testi bu makinede Docker
  ARM64 kısıtı yüzünden **koşamadı** — önceden bilinen, bu fazdan bağımsız
  bir yerel kısıt; SQLite ve PostgreSQL sözleşme testleri aynı sözleşmeyi
  eksiksiz doğruladı)
- `dotnet pack AgentPrism.slnx -c Release --no-build` → 51 `.nupkg`, hata yok (yeni paket yok, sayı sabit)
- `dotnet format AgentPrism.slnx --verify-no-changes --no-restore` → değişiklik yok
- `secret` taraması → boş (iki eşleşme `docs/51-*.md` ve `docs/hafiza/sql-server-yerel-test.md`'de,
  bu fazda dokunulmamış, önceden bilinen değişken-adı yanlış pozitifi)

## Bitiş Ölçütleri (DoD) — kapanış

- [x] Kuyruğa alınan bir çalıştırma onay ister, konsoldan onaylanır ve tamamlanır — `samples/AgentPrism.Api` üzerinde gerçek OpenAI modeliyle uçtan uca doğrulandı: kuyruğa alınan `run` gerçek `cancel_order` tool çağrısıyla `AwaitingApproval`'a düştü, `GET /api/approvals/pending` görünür oldu, `POST /decide` sonrası yeni bir `run` otomatik kuyruğa girdi ve `Completed`'a ulaştı, olay akışında tool'un gerçekten çalıştığı görüldü
- [x] `GET /api/approvals/pending` yalnız çağıranın kiracısının onaylarını döner — `ApprovalEndpointTests.Baska_kiracinin_onayi_gorunmez`, `PendingApprovalStoreContract` kiracı testleri
- [x] `Reader` rolü karar veremez (`403`) — `ApprovalEndpointTests.Reader_rolu_karar_veremez`
- [x] Aynı onaya ikinci karar `409` alır — `ApprovalEndpointTests.Ayni_onaya_ikinci_karar_409_alir`, live doğrulamada tekrarlandı
- [x] Süresi geçen onay `Expired`, çalıştırma `Failed` — `PendingApprovalStoreContract.Suresi_dolan_istek_kapatilir_ve_dondurulur`, `ApprovalExpirationService`
- [x] Her karar `audit_log`'da görünür; denetim izi yazılamazsa karar uygulanmaz — `ApprovalEndpoints.WriteAuditOrThrowAsync` (K-089/K-370), sözleşme testiyle dolaylı kapatıldı
- [x] Senkron Playground onay akışı hiç değişmeden çalışır — `ToolApprovalResolver` dokunulmadı; `SuspendOnApproval` yalnız kuyruk yolunda `true` (K-372)
- [x] Süre sonu servisi `SchemaReadyGate`'i bekler (K-354) — `ApprovalExpirationService`, `RunReconciliationService` deseni birebir kopyalandı
- [x] Dört doğrulama kapısı sıfır uyarı verir — yukarıdaki kanıt bölümü
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bu bölüm
- [x] `secret` taraması boş döndü — yukarıdaki kanıt bölümü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — 162,6 KB gzip / 250 KB (87,4 KB kalan)

## Sonraki Faza Devir Notu

- **`pending_approvals` bir izdüşümdür, sahip değildir.** Tek gerçek kaynak
  MAF'ın oturum durumudur. Bu tabloya dokunacak her yeni kod önce bu satırı
  okumalı: karar YALNIZ oturuma yazılır, tablo yalnız operatörün görmesi için var.
- **Senkron yol hâlâ mailbox'a katılmıyor (K-372).** Playground'un bekleyen
  onayları göstermesi istenirse `pending_approvals`'a yazmadan, oturum
  durumundan salt-okunur bir izdüşüm eklenmeli — mevcut yazma yoluna dokunma.
- **`ApprovalResumeJobHandler`, `ToolApprovalResolver`'ı yeniden kullanmaz** —
  minimal, kendi içinde `ChatHistoryProvider.InvokingContext`'i okuyan bir eşleştirme
  taşır (MAAI001 bastırılmış). İki yol arasında davranış sapması olursa önce
  bu iki dosya karşılaştırılmalı: `Internal/ToolApprovalResolver.cs` ve
  `Core/Approvals/ApprovalResumeJobHandler.cs`.
- **F-87 (kayıtlarda redaksiyon) ile örtüşme var.** `PendingApproval.Arguments`
  bugün yalnız `RunReconciliationOptions`/`AgentPrismRunRecordingOptions.RecordToolPayloads`
  ayarına uyuyor; F-87 karara bağlanırsa bu alan da onun kapsamına girmeli.
- **Bir sonraki faz henüz seçilmedi** — `docs/ADAYLAR.md`'den
  seçim yapılacaksa `faz-planlama` skill'i uygulanır; bu doküman kendi
  başına yeterlidir, ek okuma gerektirmez.
