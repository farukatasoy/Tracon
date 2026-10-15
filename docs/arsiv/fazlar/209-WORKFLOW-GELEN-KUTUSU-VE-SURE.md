# Faz 209 — Workflow Gelen Kutusu ve İstek Süresi

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** onaylandı (2026-10-14) — 🔒1 **B** (`workflow_requests` projeksiyonu, K-128 yeni gerekçeyle açılır) · 🔒2 **B** (`(oturum, requestId)` başına mantıksal istek) · 🔒3 **A** (`Failed` + `WorkflowRequestExpired`) · 🔒4 **A** (cevaplanmış bekleyen istek `Completed` ile kapanır) · 🔒5 **A** (workflow başına `RequestTimeout`, `RequestedAt`'ten) · 🔒6 **A** (`Reader` + `RunAccess.Read`). Açık Soru 7-11 önerileriyle uygulanır (7 B, 8 A, 9 A, 10 A, 11 A). Faz 208 kapandı (`arsiv/fazlar/208-*`)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-319** (gelen kutusu ve süre yarısı; karar yarısı [Faz 208](208-WORKFLOW-KARAR-SOZLESMESI.md))
> **Genellik:** Tracon HITL'ini kullanan her host etkilenir. Kalem tek tüketici turundan geldi (video tüketicisi, 2026-10-11); tüketici geri bildirimi kuralı uygulanır. İkinci kanıt ilk ilkedir: (1) retention bekleyen run'ın istek olayını, checkpoint'lerini ve girdi satırını süresiz tutar (K-888) ve K-130 gereği cevaplanan istek de bekler — bu satırlar **hiç** silinmez (`RetentionTargetRegistry.cs:122-144`); tüketici bunu süre olmadan sınırlayamaz; (2) yönetişim eşitliği: tool onayının kiracı geneli kutusu ve süresi var (`GET /api/approvals/pending`, `ApprovalExpirationService`), workflow isteğinin yok. Ekosistem sinyali: MCP Tasks `input_required` görevi bir yaşam süresiyle (TTL) tanımlar
> **Karşı görüş:** "Onay tüketicinin ekranında tutulabilir." Liste için kısmen doğru: tüketici `GET /api/runs?status=AwaitingInput&kind=Workflow` + run başına `/requests` ile bir liste kurabilir (N+1 çağrı). Ama "yalnız bekleyen" süzgeci sayfalamadan sonra uygulanırsa `skip/take` bozulur (`RunEndpoints.cs:64-67` kuralı); cevap bilgisi yalnız Faz 208'in tablosundadır. Süre ise yalnız Tracon'da kurulabilir: bekleyen run'ı kapatıp retention'ın satırları bırakmasını yalnız Tracon sağlar
> **Önkoşul:** [Faz 208](208-WORKFLOW-KARAR-SOZLESMESI.md) — "yanıtlandı" işareti 208'in `workflow_responses` kaydını okur; iki faz `WorkflowEndpoints.cs`, `WorkflowRunner.cs`, `WorkflowDefinition` ve `WorkflowRegistrationOptions`'ı paylaşır
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.UI`, `.Testing.Contracts.Xunit`, `Tracon.Client` (üretilen); projeksiyon seçilirse `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`
> **Yeni paket:** Yok · **Migration:** ölçüme bağlı (Açık Soru 1) — projeksiyon seçilirse üç sağlayıcı; numara uygulama anında alınır
> **Public API:** büyüyor, `1.0.0` GA'dan (K-603) önce ucuz — ölçüldü (2026-10-14): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır
> **Tüketici yüzeyi:** site: `concepts/workflows.md` ("Asking a human" `:883-916`, checkpoint ve retention paragrafı `:844-858`), `reference/configuration.md:526-529` (bekleyen run'ın retention istisnası), `ui.md` (Approvals bölümü `:326-347` + ekran görüntüsü), `capabilities.md` · sevk edilen: `WorkflowDefinition`/`AddWorkflow` XML `<example>`, `src/Tracon.Workflows/README.md`, `CHANGELOG.md`, TS istemcisi, `RetentionTargetRegistry.cs:129` yorumu ("an expiry for them is F-319's scope")
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md` (`MT-WF-*`), `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` (`MT-SEC-*`), `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` (`MT-RET-*`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 5b09e270:docs/arsiv/fazlar/209-WORKFLOW-GELEN-KUTUSU-VE-SURE.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün bekleyen insan istekleri yalnız run başına okunur. Bir host, n revizyonlu onayı tek bir listeden yönetemez. Cevaplanmış istek de K-130 gereği "bekliyor" görünür. İsteğin bir süresi yoktur: retention bekleyen run'ın satırlarını süresiz tutar ve cevaplanan istek de bekler, yani bu satırlar hiç silinmez (K-888). Bu faz iki şey getirir.

## Bitiş Ölçütleri (DoD)

- [x] §209.1 ölçümü üç sağlayıcıda yapıldı; sayılar ve Açık Soru 1'in kararı "Plandan Sapmalar"da — A olay taraması run başına olay sayısıyla büyür (SQLite 3.3 → 54.1 ms, SQL Server 46.3 → 118.7 ms, PostgreSQL 21.8 → 40.7 ms); B düz kalır; B seçildi
- [x] `GET /api/workflows/requests` kiracının bekleyen isteklerini sayfalı döner; `state=pending` sayfaları doğru toplar; cevaplanan istek `answered: true` — `WorkflowRequestInboxHttpTests` (28), `WorkflowRequestStoreContract.The_state_filter_runs_before_the_page_so_a_page_is_never_short` (4 depo); örnek uygulama: 3 öğe → cevap sonrası `pending` 2, `answered` 1, `take=1&skip=1` 1
- [x] Başka kiracının isteği listede yok; reddeden yetki işleyicisi listeyi `403` ile reddeder — `A_tenant_sees_only_its_own_requests`, `A_handler_that_refuses_reading_runs_rejects_the_list_with_403_and_never_filters_it`, `TenantIsolationContract` (4 depo)
- [x] `RequestTimeout`'u dolan istek kapanır (Açık Soru 3-4 kararıyla); süre dolmuş run'a `respond` `400` — örnek uygulama: cevapsız run vadeden 1.2 sn sonra `Failed` + `WorkflowRequestExpired`, `respond` `400` ve yeni run yok (workflow run sayısı 10 → 10); cevaplı iki run `Completed`, hatasız
- [x] Süre dolan run'ın istek olayı, checkpoint'leri ve girdi satırı bir sonraki retention turunda silinir — `What_a_waiting_run_keeps_is_kept_until_its_request_expires_and_released_after` (olay ve checkpoint; girdi satırı yalnız parametre bildiren workflow'larda vardır, aynı `runs.status` süzgeci `RunInputRetentionTests`'te), `WorkflowRequestRetentionTests` ×3
- [x] K-130 ve K-888 satırları güncellendi; K-128 yeni gerekçeyle açıldı (kategori: kalıcı-veri) — K-1000…K-1005
- [x] Dört doğrulama kapısı sıfır uyarı verir — *Kapanış kapısı* altında
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1, 2, 4, 5, 174), çıktı belgede — *Örnek uygulama koşumu*
- [x] `secret` taraması boş döndü — `kapi.py kapanis`'in ilk komutu (`tarama`), *Kapanış kapısı* altında
- [x] Manuel kabul case'leri `15-WORKFLOWS.md` (`MT-WF-165`…`174`), `13-KIRACI-VE-GUVENLIK.md` (`MT-SEC-222`, `223`), `23-SAKLAMA-ARSIV-KOTA.md` (`MT-RET-085`, `086`) içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (0 🔴, 5 🟡 kapandı)
- [x] `docs-site/` güncellendi (`concepts/workflow-requests.md` yeni; beş sayfa daha); `npm run check` temiz — *Kapanış kapısı* altında
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı gzip KB olarak ölçüldü: **211.9 KB** (Faz 208 sonu 209.8 KB; pay +2.1 KB; bütçe 250 KB)

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*WorkflowRequestInboxHttpTests" "*WorkflowRequestExpirationHttpTests"

curl -s "http://localhost:5081/tracon/api/workflows/requests?state=pending&take=50" -H "Authorization: Bearer $TOKEN"
```

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez** — sonraki oturumun en değerli bilgisidir.
> §209.1'in ölçümü aşağıdadır.

### §209.1 ölçümü (2026-10-14, taban `55ffa387`)

Yöntem: 50 bekleyen workflow run'ı tohumlanır (run başına 10 / 100 / 1000 olay: `MessageDelta` yığını + bir `WorkflowRequest` + `RunAwaitingInput`). **A** her run'ın olaylarını okuyup istek olayını süzer (sayfa başına 50 okuma). **B** `workflow_requests` satırlarından bir sayfa (`take = 50`) okur. Her sağlayıcıda gerçek veritabanı. Ölçüm kodu atılmıştır (tek seferlik prob, depoda yoktur).

| Sağlayıcı | Olay / run | A: olay taraması | B: projeksiyon |
|---|---:|---:|---:|
| SQLite | 10 | 3.3 ms | 1.5 ms |
| SQLite | 100 | 10.7 ms | 1.6 ms |
| SQLite | 1000 | 54.1 ms | 1.3 ms |
| PostgreSQL | 10 | 21.8 ms | 19.2 ms |
| PostgreSQL | 100 | 24.7 ms | 17.6 ms |
| PostgreSQL | 1000 | 40.7 ms | 17.0 ms |
| SQL Server | 10 | 46.3 ms | 40.0 ms |
| SQL Server | 100 | 45.3 ms | 34.0 ms |
| SQL Server | 1000 | 118.7 ms | 38.4 ms |

Süre taraması (`ExpireAsync`, 10 000 açık satır): vadesi gelen yokken ortanca tur SQLite 0.1 ms, PostgreSQL 0.4 ms, SQL Server 1.9 ms. Vadesi gelen 100 satırı kapatmak SQLite 7.7 ms, PostgreSQL 1.9 ms, SQL Server 11.9 ms. Örnek uygulamanın `summarize-and-approve` run'ı 28–35 olay yazdı.

**Sonuç:** A'nın maliyeti run başına olay sayısıyla büyür (SQLite 16×, SQL Server 2.6×, PostgreSQL 1.9× 10 → 1000 olay arasında); B düzdür. İşlevsel ölçüt zaten B'yi gerektiriyordu (süzgeç sayfadan önce, süre indeksi). 🔒1 **B** kesinleşti; A'nın sayfa başına 50 ayrı okuması hiçbir sağlayıcıda B'den hızlı çıkmadı.

### Planda olmayan ya da planı değiştiren kararlar

1. **K-128 yeniden açıldı (K-1000).** İstek yükü (istem, form) olayda **kalır**; satır onu `event_seq` ile adlandırır, istemi kopyalamaz — kopya içerik korumasını atlardı (K-325, K-899).
2. **Depo sözleşmesi taslaktan farklı.** `AddAsync` toplu (bir run birkaç istek yayınlar); **`ListByRunAsync`** ve **`ListStrandedAsync`** eklendi; `MarkAnsweredAsync` etkilenen satır sayısını döner; `ExpireAsync` **kiracıdan bağımsız** tek `UPDATE … RETURNING` / `OUTPUT` (`[TenantAgnostic]`, servis her satır için `AmbientTenantScope` açar).
3. **Satır `(run, istek)` başınadır;** mantıksal istek (`(oturum, requestId)`) listede `NOT EXISTS` ile türer: en yeni **açık** satır görünür. `MarkAnsweredAsync` o isteğin bütün açık satırlarını işaretler, kapalıya dokunmaz (K-1001).
4. **Satır tek yerde yazılır:** `WorkflowRunner.CompleteAsync` (`AwaitingInput`), best-effort. Plan "uyarı ve metrik" dedi — **metrik eklenmedi**. Satırı yazılamayan istek listede yok ve **hiç dolmaz**; run'dan cevaplanır (F-535).
5. **`expires_at` yazım anında dondurulur** (`RequestedAt + RequestTimeout`, kayıtlı sürümden; K-988 emsali, K-1002).
6. **`state` dize okunur,** yalnız üç ad kabul edilir (minimal API enum bağlayıcısı harf duyarlı: `state=all` `400` verdi; `Enum.TryParse` ise `1` ve `pending,answered`'ı da alırdı).
7. **`requests` ayrılmış workflow adı** (`functions` gibi): rota `/api/workflows/{name}`'den önce eşleşir (K-1005). Plan görmemişti.
8. **Süre servisi emsalden üç yerde ayrılır.** (a) `CompleteRunAsync` `EventCount`, `Usage`, `Cost`, `NonTokenCost`'u **değiştirdiği** için run önce okunur, değerler geri yazılır; yalnız `AwaitingInput` run kapatılır. (b) Run kapatılamazsa bellekte en çok 10 tur denenir. (c) 🚨 Süreç iki yazımın arasında durursa kapalı satır bir daha bulunmaz: ilk uygulama bunu "belgeli sınır" bıraktı, denetim 🟡1 planın "sonraki örnek kalanı kapatır" cümlesini karşılamadığını gösterdi → `ListStrandedAsync` ve lease'te koşan tarama (aşağıda).
9. **Geriye dönük doldurma yok** (Açık Soru 7-B): Faz 209 öncesi bekleyen run'lar listede yok ve dolmaz; run başına `/requests` çalışır. Sitede ve `CHANGELOG`'da yazılı.
10. **Sayfa başına olay okuması N+1 kaldı** (sayfa en çok 200 öğe; F-534). `WorkflowDescriptor.RequestTimeout` planda yoktu, katalog süreyi de taşır.
11. **Retention hedefi `workflow_requests`:** yalnız kapalı satır, `closed_at`'e göre, 30 gün; açık satır silinmez ve `MaxRows`'a sayılmaz (K-1004).
12. **Örnek uygulamaya `summarize-and-approve-timed` eklendi** (2 dk sınır; manuel case 4 için). E2E sunucusunun `approval-flow` grafı 2 günlük süre aldı. Ekran görüntüleri (45) ve `.ui-source.sha256` yenilendi; `workflows.md` gzip tavana (59 000 B) 58 983 B ile yakın olduğundan anlatı `concepts/workflow-requests.md`'ye konuldu.
13. **Migration numaraları:** PostgreSQL `0061`, SQL Server `0049`, SQLite `0048` (ankraj `bc5be676`). Örnek uygulamanın ilk koşusu yerel `agentprism` şemasına `0060` ve `0061`'i uyguladı; geri alma yok.

### Denetim sonrası eklenenler (2026-10-14)

- **`IWorkflowRequestStore.ListStrandedAsync(max)`** (`[TenantAgnostic]`): kapalı satırı olan, açık satırı kalmamış, `runs.status = AwaitingInput` run'ın kapalı satırları. Tek `SELECT … JOIN runs … NOT EXISTS`, üç sağlayıcıda aynı metin (`LIMIT` / `TOP` farkı); bellek içi depo boş döner; migration yok.
- **Tarama:** lease alındığında bir kez, tam tur dönerse sonraki turlarda yine bakar; bulunan run'lar aynı yolla kapanır. Lease kaybolunca bayrak sıfırlanır.
- **Sözleşmeler:** `WorkflowRequestStoreContract` +3 case (`SetRunStatusAsync` kancası), `WorkflowDefinitionStoreContract` +1 case (`The_request_timeout_survives_the_round_trip`) — ikisi sevk edilen sözleşme.
- Sitede *An answer that arrives with the time limit*; K-1003 satırı ve geçmişi; **MT-WF-174**.

### Süre servisinin emsali — bulunan kusur (giderildi)

Plan `ApprovalExpirationService`'i "birebir desen" olarak gösterdi. Desen okunurken şu kusur çıktı: süresi dolan onayın run'ı `CompleteRunAsync` ile kapatılırken run'ın **`EventCount`, `Usage`, `Cost` ve `NonTokenCost` değerleri sıfırlanıyordu** (`CompleteRunAsync` bu kolonları değiştirir, servis önce okumuyordu). Kapanan her süresi dolmuş onay, run'ın olay sayısını ve maliyetini siler; panolar ve stat'lar bunu gösterirdi. Önce kırmızı test (`ApprovalExpirationServiceTests`, 3 case) yazıldı, düştüğü görüldü, servis run'ı okuyup değerleri geri yazacak biçimde düzeltildi (`ebe1f83c`, `CHANGELOG` *Fixed*). Süre servisi aynı deseni baştan doğru kurdu.

### Örnek uygulama koşumu (2026-10-14)

`samples/Tracon.Api`, geçici SQLite deposu (`Tracon__Sqlite__ConnectionString` scratchpad'e), port 5081, `Tracon__Ui__AuthToken` tek kullanımlık, gerçek model çağrısı. Açılışta 48 SQLite migration uygulandı (`0048_workflow_requests` dahil). İlk deneme ortak PostgreSQL'e bağlandı (kullanıcı sırları) ve jeton istedi — durdurulup geçici SQLite ile yeniden açıldı; ortak veritabanına yalnız yukarıdaki iki migration uygulanmıştı.

| Manuel case | Gözlem |
|---|---|
| 1, 2 — üç bekleyen run, biri cevaplı | `state=all` → 3 öğe (üç oturum); cevap sonrası `pending` 2, `answered` 1 (`answered: true`, `lastResponseRunId` dolu), `take=1&skip=1` → 1 |
| 4 — `summarize-and-approve-timed` (2 dk), cevapsız run | `expiresAt` = `requestedAt` + tam 2 dk. Vadeden 1.2 sn sonra `Failed`: `eventCount 32`, `error.type = WorkflowRequestExpired`, `error.class = Timeout`, ileti istek kimliğini ve vadeyi söyler |
| Cevaplanmış iki zamanlı run | `Completed`, `error = null`, vadeden 2–3 sn sonra; ilk run'ın `eventCount`'u kapanıştan önce de sonra da 35 |
| 5 — süresi dolan run'a `respond` | `400` `…is not awaiting human input (status: Failed)`; workflow run sayısı 10 → 10; liste 3 öğeye indi |
| Tanım sınırları, ayrılmış ad, hata yolları | `requestTimeout` `00:00:30` ve `400.00:00:00` → `400`, `06:00:00` → `200` (geri okunur; süresiz kayıt → `null`); `PUT requests` / `Requests` / `functions` → `400`; `state=waiting` → `400`; jetonsuz → `401` |
| 174 — süreç iki yazımın arasında durdu | Tarama saatte bire ayarlı; zamanlı run `AwaitingInput` (`eventCount 38`). Uygulama durduruldu, satır `sqlite3` ile kapatıldı (run bekliyor, liste boş). Yeniden başlatınca günlükte `1 workflow request rows were closed but their runs still wait` ve run `Failed` + `WorkflowRequestExpired`, `eventCount` 38 |

Manuel case 3 (konsol, satıra tıkla) ve 6/7 ayrı yerlerde kanıtlandı: 3 → E2E `ApprovalTests` + ekran görüntüsü (👤 göz denetimi kaldı: `MT-WF-166`), 6 → `WorkflowRequestExpirationHttpTests` + üç sağlayıcıda `WorkflowRequestRetentionTests`, 7 → `TenantIsolationContract` + `WorkflowRequestInboxHttpTests` iki kiracı.

### Önce kırmızı kaydı (dürüst)

| Test | Nasıl kırmızı görüldü |
|---|---|
| `ApprovalExpirationServiceTests` | Gerçek kırmızı: onay süresi dolunca run'ın olay sayısı, kullanımı, maliyeti siliniyordu |
| `WorkflowRequestStoreContract` | Gerçek kırmızı ilk koşumda: SQLite `RETURNING w.run_id` takma adı bulamadı; `RETURNING` sırası tanımsız → küme karşılaştırması |
| `state=all` (`WorkflowRequestInboxHttpTests`) | Gerçek kırmızı: `400` (enum bağlayıcısı harf duyarlı) |
| Kurtarma servis ve restart testleri, `The_request_timeout_survives_the_round_trip`, kiracılı onay case'i | **Mutasyonla görüldü:** ilgili kod devre dışı → kırmızı (servis 4, HTTP 2, SQLite 1, onay 1); geri alındı |
| Mimari kapılar, taban çizgileri, SQL anlık görüntüsü, kapsama listeleri | Yeni yüzey önce kırmızı yaptı, sonra güncellendi |
| Kalan yeni sınıflar | Önce kırmızı kaydı tutulmadı; kod ve test birlikte yazıldı, mutasyonla sınanmadı |

### Kapanış kapısı (2026-10-14)

- `kapi.py kapanis --taban 55ffa387` (`20c35365`) ✅ çıkış 0: tarama, `--denetle`, betik testleri, agent haritası, denetim paketi, build (85 sn, **0 uyarı**), tam test (564 sn), `dotnet pack`, `dotnet format --verify-no-changes`, `npm run check` (1108 sayfa, kırık bağlantı 0, en ağır `concepts/workflows` 58 983 B / 59 000).
- Yolda kırmızı görülenler (hepsi düzeltildi): `dotnet test Tracon.slnx` iki mimari kapıda düştü — `TestDelayClassificationTests` (etiketsiz `Task.Delay`) ve `PlaywrightLocatorTests` (üç riskli locator, `Exact = true`); ikisi de önceki commit'lerdendi, kapıyı o commit'ler koşmamıştı. `build-agent-map.mjs --check` ilk koşuda `llms-full.txt` eskidiği için düştü (yenilendi).
- `kapi.py yayin --kuru --surum 1.0.0-preview.4` ✅ çıkış 0: 20 paket, kırıcı liste 5 tip (hepsi `Unreleased`'de), `npm publish --dry-run`, 6 exact-version örnek, Native AOT ve net8.0 tüketici smoke. İlk koşum eski aday dizini yüzünden durdu (gitignore'lu `artifacts/package/yayin/1.0.0-preview.4`, Faz 208 provasından); araç önerdiği gibi silindi.
- `dokuman-bakim.py --site-denetle --taban 55ffa387 --site-gerekce-yazildi` ✅ (gerekçe *Site gerekçesi* altında).

## Bu Fazda Verilen Kararlar

| Karar | Kayıt |
|---|---|
| `workflow_requests` projeksiyonu açıldı; istek yükü olayda kalır, satır onu `event_seq` ile adlandırır; yazım `CompleteAsync`'te, best-effort; geriye dönük doldurma yok. K-128 yeniden açıldı (kullanıcı kararı) | **K-1000** *(kalıcı-veri, public-api)*; K-128 satırı yerinde annotated |
| Liste `(oturum, requestId)` başına bir mantıksal istektir; süzgeç sayfadan önce; "yanıtlandı" `MarkAnsweredAsync` ile; sıra `RequestedAt` (kullanıcı kararı) | **K-1001** *(public-api)* |
| Workflow başına `RequestTimeout` (1 dk – 365 gün), `RequestedAt`'ten sayılır, yazım anında `expires_at`'e sabitlenir (kullanıcı kararı) | **K-1002** *(public-api)* |
| Süre dolunca kapanış: cevapsız istek → `Failed` + `WorkflowRequestExpired` (`Timeout`); hepsi cevaplı → `Completed`, hatasız; K-130 istisnası (kullanıcı kararı) | **K-1003** *(kalıcı-veri, geri-dönüşü-pahalı)*; K-130 ve K-888 satırları işaretli |
| `RetentionTargets.WorkflowRequests`: yalnız kapalı satır, `closed_at`'e göre, 30 gün; K-888 muafiyetleri run kapanınca kendiliğinden serbest kalır | **K-1004** *(kalıcı-veri)* |
| Liste yetkisi `Reader` + `RunAccess.Read`; reddeden işleyici listeyi `403` ile reddeder; `requests` ayrılmış ad (kullanıcı kararı) | **K-1005** *(güvenlik, public-api)* |
| Kapatılamayan run bellekte en çok 10 tur denenir; süreç iki yazımın arasında durursa lease'i alan örnek `ListStrandedAsync` ile bulup kapatır (bağımsız denetim 🟡1 sonrası) | K-1003 içinde (satırı ve geçmişi güncellendi) |
| Kategori dışı: satır yazımı başarısız olursa yalnız uyarı loglanır, metrik yoktur | Yalnız burada (planda "uyarı ve metrik" idi) |
| Kategori dışı: sayfa başına olay okuması N+1 kalır; sayfa en çok 200 | Yalnız burada; ADAYLAR'a aday |
| Kategori dışı: `state` sorgu parametresi dize okunur (minimal API enum bağlayıcısı büyük/küçük harfe duyarlı) | Yalnız burada |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (altı 🔒 cevabı belge başlığına işlendi; plan gövdesi yeniden yazılmadı, sapmalar *Plandan Sapmalar*'da) |
| Düzeltme turu sayısı | 1 (bağımsız denetimin beş 🟡'sinin ve bir 🟢'sinin düzeltmesi; kapılar yeniden koşuldu) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 (denetim 🔴 bulmadı; beş 🟡'nin dördü gerçek, biri belgelenen test boşluğu — hepsi kapandı) |
| Fazın ürettiği regresyon | 0 (mevcut davranışı bozan bulunmadı). Bulunan **mevcut** kusur: `ApprovalExpirationService` süresi dolan onayın run sayaçlarını siliyordu — giderildi |
| Faz kapandıktan sonra bulunan kusur | 0 (kapanış anı; sonraki `kusur-giderme` koşumu günceller) |

## Denetim Bulguları

Bağımsız `faz-denetim` (`faz-denetcisi`, salt-okunur), 2026-10-14: **0 🔴**, 5 🟡, 7 🟢. Denetçi kiracı sınırını, yetki kapısını, üç sağlayıcının migration ve sorgu eşdeğerliğini, retention muafiyetlerini, uyumluluğu ve `K-1000…K-1005` ile kodun çelişmediğini kanıtla doğruladı.

| Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|
| Satır kapanır, sonra run kapanır; süreç arada durursa run `AwaitingInput` kalır, kapalı satır bir daha bulunmaz (planın "sonraki örnek kalanı kapatır" cümlesi uygulanmamış; K-1003 sınırı kullanıcıya sorulmadan "kullanıcı kararı" altında) | 🟡1 | gerçek | **Düzeltildi:** `IWorkflowRequestStore.ListStrandedAsync` + servisin lease'te koşan taraması; dört depoda sözleşme case'i, dört servis testi, iki uçtan uca restart testi (mutasyonla görüldü); K-1003 satırı düzeltildi. *Plandan Sapmalar* 9(c) |
| Cevap ile süre dolumu yarışı test edilmemiş ve sitede yazılmamış | 🟡2 | gerçek | **Düzeltildi:** akışlı yol için pencere testi (satır kapalı, run bekliyor → cevap alınır, işi koşar, işaret 0 satır değiştirir, run sonra `Failed` + `WorkflowRequestExpired`); sitede *An answer that arrives with the time limit*. Kuyruklu yol testle sabitlenmedi: iş süre dolduktan sonra başlarsa `WorkflowRunJobHandler` kaynağı reddeden isteği kendi run'ını `Failed` kapatır (kod okuması, `RefuseAsync`); sitede yazılı |
| `RequestTimeout`'un `WorkflowDefinitionPayload` gidiş-dönüşü SQL depolarda sözleşmeyle sabit değil | 🟡3 | gerçek | **Düzeltildi:** `The_request_timeout_survives_the_round_trip` (`GetAsync`, `ListAsync`, `GetVersionAsync`, `RollbackAsync`); payload'ı `null` yazınca kırmızı (mutasyon). Sevk edilen sözleşme genişledi → yayın provası |
| Workflow süre servisinin testi yalnız `EventCount`'u sabitliyor; `Usage`/`Cost`/`NonTokenCost` değil | 🟡4 | gerçek | **Düzeltildi:** `The_usage_cost_and_non_token_cost_of_the_run_survive_the_close` |
| `ApprovalExpirationService` ortam kiracısına bağımlı (onay düzeltmesinde doğdu), kiracılı test yok | 🟡5 | gerçek | **Düzeltildi:** `The_run_of_a_tenant_is_closed_although_the_scan_runs_in_no_tenants_request`; kapsam satırı kaldırılınca kırmızı (mutasyon) |
| `?state=` ayrıştırması gevşek: `1`, `pending,answered`, `pending,all` kabul edilir; site "üçünden biri değilse `400`" der | 🟢1 | gerçek | **Düzeltildi** (ucuzdu): yalnız ad kabul edilir; `A_state_that_is_not_one_of_the_three_names_is_refused` (5 satır) |
| Süre kapanışı normal kapanışın `KeepCheckpointsAfterCompletion = false` temizliğini ve `RecordRun` metriğini atlar | 🟢2 | gerçek, düşük | Devredildi: **F-532** (onay emsali aynı; retention yolu açık) |
| `What_a_waiting_run_keeps…` yalnız `RunEvents` ve `WorkflowCheckpoints`'ı sınar; `RunInputs` sınanmaz | 🟢3 | gerçek, düşük | **Gerekçelendi:** denendi, `run_inputs` satırı yalnız parametre bildiren workflow'lar için yazılır (`BindParametersAsync`), testin graflarında satır yok. Filtre aynı `runs.status` koşuludur ve `RunInputRetentionTests` `AwaitingInput` muafiyetini ayrı kanıtlar |
| §209.1 ölçüm düzeneği depoda yok; sayılar yeniden üretilemez | 🟢4 | gerçek | **Gerekçelendi:** tek seferlik prob atıldı; yöntem *Plandan Sapmalar*'da yazılı (50 run, 10/100/1000 olay, A ve B). Kalıcı kapasite düzeneği kapsam dışı |
| Kısmen dolmuş run'da kapanmış isteğe cevap alınır (kapı satır durumuna bakmaz; işaret kapalı satıra yazılmaz) | 🟢5 | gerçek, dar pencere | Devredildi: **F-533** |
| `MaxCloseAttempts` vazgeçme dalı ve iptal yolu testsiz | 🟢6 | gerçek | Vazgeçme dalı **eklendi** (`A_run_that_cannot_be_closed_is_given_up_after_ten_scans_and_logged_as_an_error`), ayrıca kurtarma taraması bu run'ı bir sonraki lease'te yeniden dener. İptal yolu testsiz kaldı: servis iptalde dönen tek `return`'dür |
| `--site-denetle` ❌: `kalicilik` (`PostgresQueries.cs`), `paket-readme` (`Tracon.AspNetCore/README.md`) | 🟢7 | gürültü | **Gerekçelendi** (aşağıda *Site gerekçesi*) |

### Site gerekçesi (`--site-gerekce-yazildi`)

- `kalicilik` → `getting-started/persistence.md`: migration yalnız yeni tablo `workflow_requests` ekler. Sayfa tablo listesi ya da tek yönlü migration örneği taşımaz, retention'dan "run olayları 30 gün, span'ler 14 gün gibi" örnekle söz eder. Tablonun ve hedefin kuralları `concepts/workflow-requests.md` ve `reference/configuration.md`'dedir (ikisi de güncellendi).
- `paket-readme` → `packages.md`: `Tracon.AspNetCore` README'sinde yalnız işlem sayısı değişti (175 → 176, 137 → 138 yol); `packages.md` sayı taşımaz.

## Sonraki Faza Devir Notu

Sıradaki faz seçilmedi; aday sırası [`ADAYLAR.md`](../../ADAYLAR.md)'dedir (Faz 209 için F-319 satırı çıkarıldı). Bu faz şunları bıraktı:

- **Devralınan sözleşmeler:** yukarıdaki *Gerçekleşen Public API*. `IWorkflowRequestStore` altı üyeli; yeni bir depo uygulayan `WorkflowRequestStoreContract`'ı geçmelidir (`SetRunStatusAsync` kancasını run tablosu varsa uygulamalıdır). Satır tek yerde yazılır: `WorkflowRunner.CompleteAsync` → `RecordRequestsAsync`.
- 🚨 **Bilinen tuzaklar** (ayrıntı [`hafiza/workflow-cevaplari.md`](../../hafiza/workflow-cevaplari.md)): `CompleteRunAsync` sayaçları **değiştirir** (kapatırken önce oku, geri yaz); arka plan servisi kiracısız koşar (`AmbientTenantScope.Begin`, test host'unda sına); "satırı kapat sonra bir şey yap" adımının çökme boşluğu `ListStrandedAsync`'le kapatıldı; `UPDATE … RETURNING` sırası tanımsızdır; minimal API enum bağlayıcısı harf duyarlıdır.
- 🚨 **Uygulanmış migration'lar değişmez:** PostgreSQL `0061`, SQL Server `0049`, SQLite `0048` (`applied-migrations.json` ankrajlı). Yeni sütun/indeks yeni migration ister.
- **Açık uçlar (ADAYLAR):** F-532 (süre kapanışı normal kapanış temizliğini atlar), F-533 (kapanmış isteğe cevap), F-534 (sayfa başına olay okuması), F-535 (satırı olmayan bekleyen run için uzlaştırma), F-518 (`workflow_responses` büyümesi; `workflow_requests` için kalıp hazır, K-1004).
- **Sınırlar (kullanıcıya söylenmiştir):** Faz 209 öncesi bekleyen run'lar listede yok ve dolmaz (geriye dönük doldurma yok); satırı yazılamayan istek listede yok ve dolmaz; süre ile cevap arasında iki yazımlık (ms) pencere vardır ve davranışı sabit; webhook yayınlanmaz.
- **El ile kalan:** `MT-WF-166` 👤 (konsol, göz denetimi; Vitest ve E2E karşılığı var). `MT-WF-172`, `173`, `MT-SEC-222`, `223` ☐ (➜ CI: otomatik karşılıkları var).
- **Geliştirici ortamı notu:** örnek uygulama kullanıcı sırlarıyla ortak PostgreSQL'e bağlanır; ilk koşuda yerel `agentprism` şeması `0060` ve `0061`'i aldı (geri alma yok). SQLite için `Tracon__PostgreSql__ConnectionString=""` ver.
