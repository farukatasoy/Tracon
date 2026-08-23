# Faz 54 — Öksüz Çalıştırma Uzlaştırması

> **Durum:** ✅ Tamamlandı (2026-08-09)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-36**
> **Önkoşul:** [Faz 42](42-TEK-YURUTUCU-SECIMI.md) — `ISingletonLeaseStore` · [Faz 46](46-DAYANIKLI-CALISTIRMA.md) — bu fazın kapsamını **daraltan** faz
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Sql.Shared`
> **Yeni paket:** Yok · **Migration:** gerekli — `runs` tablosuna heartbeat sütunu, numara uygulama anında alınır
> **Public API:** büyüyor — bir ayar sınıfı ve `IRunStore`'a iki metot. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Süreç düşerse `runs` satırı sonsuza dek `Running` kalır. Arayüzde asla bitmeyen çalıştırmalar birikir ve `RunStatistics.ErrorRate` hesabının paydası (`settled = CompletedRuns + FailedRuns + CanceledRuns`) bozulur: sonuçlanmamış bir satır paydaya hiç girmez ve hata oranı **yapay olarak** düşük görünür.

## Bitiş Ölçütleri (DoD)

- [x] Eşiği aşan bir `Running` satır `Failed` + `error_type = orphaned` olarak kapanır — `RunStoreContract.ClaimOrphanedRunsAsync_esigi_asan_Running_satiri_Failed_yapar` (InMemory + PostgreSQL + SQLite + SQL Server), ayrıca `samples/AgentPrism.Api` ile gerçek kanıt (aşağıda)
- [x] Eşiği **aşmayan** bir `Running` satıra dokunulmaz (yanlış pozitif yok) — `ClaimOrphanedRunsAsync_esigi_asmayan_Running_satira_dokunmaz`
- [x] `Queued` satır hiçbir koşulda uzlaştırılmaz — `ClaimOrphanedRunsAsync_Queued_satira_hicbir_kosulda_dokunmaz`
- [x] `Enabled = false` (varsayılan) iken hiçbir uzlaştırma sorgusu atılmaz — `RunReconciliationTests.Devre_disiyken_uzlastirma_hicbir_sorgu_atmadan_hemen_doner` / `...heartbeat_yazici...` (bilerek kayıtlı-ama-`MarkReady`-çağrılmamış bir `SchemaReadyGate` ile test edildi: erken çıkış gate'i beklemeden gerçekleşiyor)
- [x] İki örnekli kurulumda uzlaştırma yalnız birinde koşar — `RunReconciliationTests.Iki_ornekte_uzlastirma_yalniz_birinde_kosar` (Faz 42'nin `SingletonGuard` deseni)
- [x] Uzlaştırma sonrası `GET /api/stats` hata oranı paydası doğru — `RunReconciliationTests.Esigi_asan_calistirma_kapanir_ve_ErrorRate_paydasi_duzelir`
- [x] Kapatılan çalıştırmanın olay akışında bir `RunFailed` olayı görünür — sözleşme testi + gerçek `run`'da doğrulandı
- [x] Uzlaştırıcı `SchemaReadyGate`'i bekler (K-354) — `RunReconciliationService`/`RunHeartbeatWriter` `ExecuteAsync` ilk satırında `WaitAsync`
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/test/pack/format hepsi yeşil
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Gerçek Çalıştırma Kanıtı" altında
- [x] `secret` taraması boş döndü

### Gerçek Çalıştırma Kanıtı

`samples/AgentPrism.Api`, SQLite (`AgentPrism:Sqlite:ConnectionString`) ve kısa
aralıklarla (`HeartbeatInterval=1sn`, `OrphanThreshold=3sn`, `ScanInterval=1sn`)
gerçekten çalıştırıldı. Önce normal bir `run` (`support` agent'ı) uçtan uca
SSE ile tamamlandı (mevcut davranış bozulmadı). Ardından **çökmüş bir sürecin
bıraktığı** satırı taklit etmek için `runs` tablosuna doğrudan `started_at`'i
10 dakika geride olan bir `Running` satır eklendi (id, gerçek `StartRunAsync`
yolunun kullandığı BÜYÜK harfli uuid biçimiyle — K-191). ~4 saniye sonra:

```
$ curl -s http://localhost:5080/agentprism/api/runs/$RUN_ID | jq '.status, .error'
"Failed"
{
  "type": "orphaned",
  "message": "Calistirma yuruten surec yanit vermiyor; son isaret: 2026-08-08T22:57:09.0000000Z.",
  "class": "Infrastructure",
  "fingerprint": "orphaned"
}

$ curl -s "http://localhost:5080/agentprism/api/runs?status=Running" | jq
[]
```

Log: `warn: AgentPrism.RunReconciliationService[0] 1 oksuz calistirma kapatildi
(esik: ...)`. `eventCount: 1` ve olay akışında tek bir `RunFailed` (tip 7) kaydı
oluştu.

🚨 **Bu doğrulama, `samples/AgentPrism.Api`'de Faz 54'ten TAMAMEN bağımsız,
önceden var olan bir kusuru tesadüfen ortaya çıkardı**: `app.MapAgentPrismMcpServer()`
`app.Run()`'dan ÖNCE senkron `catalog.ListAsync()` çağırır ama migration'lar
yalnız `host.StartAsync()` (yani `app.Run()`) içinde çalışır; tamamen boş bir
veritabanı dosyasıyla ilk açılış `SqliteException: no such table` ile çöker.
Bu fazın kapsamı DIŞINDA olduğu için düzeltilmedi; doğrulama için yerel
`Program.cs`'te `MapAgentPrismMcpServer()`/`MapAgentPrismA2A()` çağrıları
GEÇİCİ olarak yorum satırına alınıp test sonrası `git checkout` ile geri
alındı (K-186/K-317'nin izlediği "geçici değiştir, doğrula, geri al" yöntemi).
Ayrıntı ve olası düzeltme yönü: `docs/hafiza/mcp-a2a-sunucu.md`.

### Doğrulama komutları

```bash
curl -s "http://localhost:5080/agentprism/api/runs?status=Running" | jq 'length'
# uzlastirma turundan sonra 0 beklenir (uc bir dizi doner, {items:[...]} DEGIL —
# plan taslaginin `.items | length` varsayimi yanlisti, gercek cikti ile duzeltildi)

curl -s http://localhost:5080/agentprism/api/runs/$RUN_ID | jq '.status, .error.type'
# "Failed", "orphaned" beklenir
```

---

## Plandan Sapmalar

1. **`TouchHeartbeatAsync` imzası `Guid runId` değil `IReadOnlyCollection<Guid> runIds` aldı** — Açık Soru 1'in kendi önerisiyle (toplu `UPDATE`) taslak imza çelişiyordu; öneri esas alındı, taslak düzeltildi (K-362).
2. **`ClaimOrphanedRunsAsync`'in `RunFailed` olayını KİM yazar sorusu, "uzlaştırıcı" ifadesinin `RunReconciliationService` mi yoksa `IRunStore` uygulaması mı olduğu belirsizdi.** Karar: `IRunStore` uygulamasının kendisi yazar (`runs` UPDATE'i ile aynı çağrıda) — `RunReconciliationService`'i olay şemasından habersiz tutar ve store'un tenant-agnostic sınırını tek yerde toplar (K-366).
3. **`error_class` için "Infrastructure" değeri plan taslağının varsaydığı gibi hazır DEĞİLDİ**; `RunErrorClass` enum'ı okununca yoktu. Yeni değer sona eklendi (K-014'ün "sayısal değer yeniden numaralanmaz" kuralıyla) — K-363.
4. **Hata parmak izi `ErrorFingerprint.Compute` ile DEĞİL, sabit `"orphaned"` dizesiyle üretildi** — o sınıf `AgentPrism.Core`'da `internal`'dır ve `AgentPrism.Sql.Shared` (ayrı derleme) ona erişemez; sabit dize hem daha basit hem üç uygulama arasında davranış eşitliğini garantiler (K-364).
5. **Dizi tabanlı `WHERE id IN (@array)` sorgusu HİÇBİR yerde kullanılmadı** — `AddUuidArray`'in SQL Server/SQLite'ta ürettiği küçük harfli JSON, `runs.id`'nin büyük harfli metniyle (K-191) sessizce uyuşmayabilirdi. `TouchHeartbeatAsync` tekil `UPDATE` döngüsüne, `ClaimOrphanedRunsAsync` ise salt-okunur bir alt sorguya (id eşitliği hiç gerekmez) yazıldı (K-365).
6. **`samples/AgentPrism.Api` ile gerçek doğrulama, fazın kapsamı dışında önceden var olan bir kusur ortaya çıkardı** (`MapAgentPrismMcpServer`'in migration'lardan önce senkron sorgu atması) — düzeltilmedi, `docs/hafiza/mcp-a2a-sunucu.md`'ye not düşüldü.

## Bu Fazda Verilen Kararlar

K-362, K-363, K-364, K-365, K-366 — tam gerekçeler `docs/KARARLAR.md`'de.

## Sonraki Faza Devir Notu

1. **`RunHeartbeatWriter`/`RunReconciliationService` ikisi de `RunReconciliationOptions.Enabled`
   tek bayrağına bağlıdır** — ikisini ayrı açıp kapatmanın bir yolu yok. Bugüne
   kadar ayrı bir talep olmadı; gerekirse `HeartbeatEnabled`/`ReconciliationEnabled`
   olarak ikiye bölünebilir (Faz 7'den önce ucuz).
2. **Elle tetikleme ucu (`POST /api/runs/reconcile`) hâlâ yok** (Açık Soru 2, YAGNI).
   Faz 33'ün teşhis ucu "kaç öksüz satır var" sorusunu `SELECT` ile cevaplayabilir
   ama kapatmaz; gerçek bir operasyonel talep gelirse eklenir.
3. **🚨 `samples/AgentPrism.Api`'de `MapAgentPrismMcpServer()`/`MapAgentPrismA2A()`
   tamamen boş bir veritabanında acilista COKER** (Faz 54'ten bağımsız, bu fazın
   doğrulamasında tesadüfen bulundu). Ayrıntı ve olası düzeltme yönü:
   `docs/hafiza/mcp-a2a-sunucu.md`. Bir sonraki fazda MCP/A2A yüzeyine dokunuluyorsa
   bu not okunmalı; aksi halde ayrı bir aday kalem olarak ele alınabilir.
4. **`RunErrorClass` artık 13 değer taşıyor** (`Infrastructure = 12` eklendi).
   Yeni bir hata sınıfı ihtiyacı doğarsa aynı desen (sona ekle, K-014) izlenir.
5. **`IRunCancellationRegistry.ActiveRunIds` genel amaçlı bir "bu süreçte şu an
   çalışan run'lar" sorgusudur** — heartbeat dışında ihtiyaç duyan gelecekteki
   bir özellik (ör. süreç-içi teşhis ucu) bu özelliği doğrudan kullanabilir,
   yeni bir defter icat etmesine gerek yok.
