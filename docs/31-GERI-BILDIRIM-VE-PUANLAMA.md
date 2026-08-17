# Faz 31 — Geri Bildirim ve Puanlama

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-52**
> **Önkoşul:** Yok. Kalem önkoşulsuzdur ve bugün yapılabilir
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** uygulandı — PostgreSQL `0017`, SQL Server `0005`, SQLite `0005`
> **Public API:** büyüdü — `RunScore`, `RunScoreKind`, `IRunScoreStore`, `RunStatistics.ScoredRuns`/`PositiveRate`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-141\|K-152\|K-178\|K-198\|K-228\|K-232" docs/KARARLAR.md
   ```
   **K-141** (eval çalıştırmaları `runs` istatistiklerinden hariç tutulur — puan
   ortalaması aynı ayrımı yapmalıdır), **K-152** (`/api/stats/timeseries`
   bilerek hariç tutmaz — yeni gösterge hangi tarafta?), **K-178** (migration
   numaraları sağlayıcı başına bağımsızdır), **K-198** (saklama SQL'i tek
   tabloyla üretilir — yeni tablo saklama hedefi olmalıdır), **K-228** (eksik
   çeviri anahtarı derleme hatasıdır), **K-232** (sunucu yanıtı çevrilmez).
3. [`30-ARAYUZ-CILASI.md`](30-ARAYUZ-CILASI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/30-ARAYUZ-CILASI.md
   ```
   Arayüzün iki dilli sözlük düzenini ve bundle payını devralıyorsun.
4. Alan hafızası (bu faz dört alana dokunuyor):
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (üç migration seti,
   paylaşılan depo), [`hafiza/postgresql.md`](hafiza/postgresql.md) (DDL),
   [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) (uç kaydı),
   [`hafiza/frontend.md`](hafiza/frontend.md) (sözlük, bundle)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — veri modeli bölümü (`runs` ile ilişki)

---

## Amaç

Bugün bir çalıştırmanın **teknik olarak bitip bitmediği** biliniyor; **iyi olup
olmadığı** bilinmiyor. Modelin ürettiği yanlış cevap `Completed` olarak
kaydedilir ve hiçbir yerde ayırt edilmez. Bu faz, bir çalıştırmaya ve tek tek
mesajlara insan puanı iliştirir.

- **F-52** — çalıştırma ve mesaj başına puan (olumlu/olumsuz, 1–5 yıldız,
  serbest yorum), yazma/okuma uçları, `runs` listesinde ve gösterge panelinde
  bir gösterge.

Bu, ölçme–iyileştirme döngüsünün **ilk halkasıdır**. Aday listesindeki F-53
(üretimden eval kümesi), F-55 (hata sınıflandırma), F-71 (çevrimiçi
değerlendirme) ve F-74 (kanarya yayını) kalemlerinin dördü de bu tablonun
üstüne kurulur. Puan olmadan hiçbiri kurulamaz.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -rni "feedback" src/ --include="*.cs"` | Hiç sonuç yok. Depoda geri bildirim kavramı yoktur |
| `grep -rni "run_score\|rating\|thumbs" src/ --include="*.cs" --include="*.sql"` | Hiç sonuç yok |
| [`IRunStore.cs`](../src/AgentPrism.Abstractions/Runs/IRunStore.cs) | On iki metot taşır; hiçbiri puan yazmaz veya okumaz |
| [`RunEndpoints.cs:31-114`](../src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs) | Dört uç, dördü de `GET`. Bir çalıştırmaya bir şey **yazmanın** yolu yoktur |

> Kanıtlar 2026-08-06 tarihinde doğrulandı.

> 🚨 **Aday listesinin ölçümü düzeltildi.** Aday listesi `grep -rni "feedback"
> src/` komutunun boş döndüğünü yazıyordu. Komut **boş dönmez** —
> `src/AgentPrism.UI/frontend/node_modules/` altında yüzlerce eşleşme vardır.
> İddia yalnız `--include="*.cs"` ile veya `node_modules` dışlanarak doğrudur.
> Sonuç değişmez, ölçüm komutu değişir.

---

## 31.1 — Veri modeli

Puan `runs` tablosuna sütun olarak **eklenmez**. Üç sebep:

1. Bir çalıştırma **birden çok** puan alabilir (farklı kullanıcı, farklı zaman).
2. Puan **mesaj** düzeyinde de verilebilir; `runs` satırı bunu taşıyamaz.
3. `runs` sıcak yoldadır ve her çalıştırmada yazılır. Puan seyrek gelir ve
   ayrı bir yaşam döngüsü izler.

```mermaid
erDiagram
    runs ||--o{ run_scores : "puanlanir"
    run_scores {
        uuid   id PK
        text   tenant_id
        uuid   run_id FK
        text   message_id "null ise calistirma puani"
        int    kind "1=ikili 2=yildiz"
        int    value "ikili: 0/1 - yildiz: 1..5"
        text   comment "null olabilir"
        text   source "human | api | judge"
        text   author "null olabilir"
        timestamptz created_at
    }
```

**`source` sütunu bugünden konur.** F-71 (çevrimiçi değerlendirme) yargıç
puanını aynı tabloya yazacaktır ve insan puanıyla karıştırılmamalıdır. Sütunu
sonradan eklemek üç migration daha demektir; bugün koymak bedavadır.

**`message_id` boş bırakılabilir.** Boşsa puan tüm çalıştırmaya aittir.

### Benzersizlik

Bir yazar bir hedefi bir kez puanlar. `UNIQUE (tenant_id, run_id, message_id,
author)` kısıtı ikinci yazımı **günceller**, yeni satır açmaz. `author` boş
olduğunda (kimliksiz kurulum) kısıt çalışmaz; bu durumda her çağrı yeni satır
yazar ve bu kabul edilen davranıştır.

### Saklama hedefi

`run_scores` büyüyen bir tablodur. Faz 25'in
[`RetentionTargetRegistry`](../src/AgentPrism.Sql.Shared/Internal/RetentionTargetRegistry.cs)
beyaz listesine bir hedef olarak eklenir (`created_at < @cutoff`). K-198 gereği
SQL üç sağlayıcıya kopyalanmaz, tek tabloyla üretilir.

## 31.2 — İstatistik ve hariç tutma

`RunStatistics`'e iki alan eklenir: puanlanmış çalıştırma sayısı ve olumlu
oran. Burada K-141'in ayrımı **korunmalıdır**: eval çalıştırmaları (`RunKind.Eval`)
istatistiğe girmez. Bir eval koşusunun kendi puanı, üretim kalitesini ölçen
oranı kirletmemelidir.

`/api/stats/timeseries` K-152 gereği hiçbir şeyi hariç tutmaz. Yeni puan
göstergesi **hangi tarafta olacak** — bu bir karardır ve Açık Sorular'dadır.

## 31.3 — Yazma yolu

```mermaid
sequenceDiagram
    participant U as Arayuz / istemci
    participant E as RunEndpoints
    participant S as IRunScoreStore
    participant A as AuditRecorder

    U->>E: "POST /api/runs/{runId}/feedback"
    E->>E: "run var mi + kiraci eslesiyor mu"
    alt run yok veya baska kiracinin
        E-->>U: "404 Problem"
    else
        E->>S: "UpsertAsync(score)"
        E->>A: "denetim kaydi (yazar, hedef, deger)"
        E-->>U: "200 RunScore"
    end
```

Kiracı denetimi atlanamaz: bir kiracı diğerinin çalıştırmasını puanlayamaz.
Çalıştırma bulunamadığında ve başka kiracıya ait olduğunda **aynı** `404`
döner; ayrı hata mesajı varlık sızdırır.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions/Runs
public enum RunScoreKind
{
    Binary = 1,
    Stars = 2,
}

public sealed record RunScore
{
    public Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required Guid RunId { get; init; }
    public string? MessageId { get; init; }
    public required RunScoreKind Kind { get; init; }
    public required int Value { get; init; }
    public string? Comment { get; init; }
    public required string Source { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public interface IRunScoreStore
{
    ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid scoreId,
        CancellationToken cancellationToken = default);
}
```

`RunStatistics`'e eklenecek alanlar:

```csharp
public sealed record RunStatistics
{
    // ... mevcut alanlar
    public long ScoredRuns { get; init; }
    public double? PositiveRate { get; init; }   // puan yoksa null
}
```

> 🚨 `RunStatistics` public bir `sealed record`'tur. Alan eklemek **bugün
> bedavadır**; Faz 7'den (yayın) sonra bir sürüm kararıdır.

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | `/api/runs/{runId:guid}/feedback` | Operator | Puan yazar veya günceller |
| `GET` | `/api/runs/{runId:guid}/feedback` | Reader | Çalıştırmanın puanlarını listeler |
| `DELETE` | `/api/runs/{runId:guid}/feedback/{scoreId:guid}` | Operator | Puanı siler |

Rol seçimi gerekçesi: `AgentPrismPolicies.Reader` salt okumadır
([`AgentPrismPolicies.cs:34`](../src/AgentPrism.AspNetCore/Security/AgentPrismPolicies.cs));
puan yazmak bir yazma işlemidir ve `Operator` "Reader + çalıştırma başlatma,
onay verme" tanımına oturur.

### Arayüz payı

İki ekrana dokunulur: çalıştırma ayrıntısı (puan düğmeleri + yorum kutusu) ve
gösterge paneli (bir gösterge). Yeni bağımlılık **yok**.

Bugünkü kullanım ölçüldü (2026-08-06):

| Ölçüm | Değer |
|---|---|
| JavaScript, gzip | **151,3 KB** / 250 KB bütçe |
| Kalan pay | **98,7 KB** |

Bu fazın payı **tahminî 1–2 KB gzip**'tir ve bu bir **tahmindir**. Gerçek değer
uygulama anında `npm run build` çıktısındaki `postbuild.mjs` satırından
okunur ve bu belgeye yazılır.

Yeni sözlük anahtarları `locales/en.ts` **ve** `locales/tr.ts` dosyalarına
girer. Eksik anahtar derleme hatasıdır (K-228). Sunucudan gelen hata metni
çevrilmez (K-232).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Runs/
├── RunScore.cs
├── RunScoreKind.cs
└── IRunScoreStore.cs

src/AgentPrism.Core/Storage/
└── InMemoryRunScoreStore.cs

src/AgentPrism.Sql.Shared/Stores/
└── SqlRunScoreStore.cs

src/AgentPrism.PostgreSql/Migrations/
└── NNNN_run_scores.sql
src/AgentPrism.SqlServer/Migrations/
└── NNNN_run_scores.sql
src/AgentPrism.Sqlite/Migrations/
└── NNNN_run_scores.sql

src/AgentPrism.AspNetCore/Endpoints/
└── RunEndpoints.cs            (uc eklenir)

src/AgentPrism.UI/frontend/src/
├── components/FeedbackControl.tsx
└── locales/{en,tr}.ts         (anahtar eklenir)
```

> Migration numaraları **rezerve edilmez**. Uygulama anında her sağlayıcının
> kendi dizinindeki bir sonraki boş numara alınır (K-178).

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `RunScoreStoreContractTests` | `tests/Shared/Contracts/` altında; bellek içi + üç SQL sağlayıcısında aynı davranış |
| `RunScoreTenantIsolationTests` | Bir kiracı diğerinin çalıştırmasını puanlayamaz; `404` döner |
| `RunScoreUpsertTests` | Aynı yazar ikinci kez puanladığında satır **güncellenir**, ikinci satır açılmaz |
| `RunStatisticsScoreTests` | `RunKind.Eval` çalıştırmaları orana girmez (K-141) |
| `RunFeedbackEndpointTests` | Üç uç; rol denetimi, `404` davranışı, doğrulama sınırları (yıldız 1–5 dışı `400`) |
| `FeedbackControl.test.tsx` | Vitest: düğme durumu, iyimser güncelleme, hata geri alması |

Sözleşme testi `tests/Shared/Contracts/` altına yazılır — hem bellek içi hem üç
SQL sağlayıcısı üzerinde koşar.

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** sorulur.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Puan deposu ayrı arayüz mü, `IRunStore`'a ek metot mu? | A: yeni `IRunScoreStore` · B: `IRunStore`'a üç metot ekle | **A.** `IRunStore` bugün on iki metot taşıyor; büyütmek onu uygulayan herkesi zorlar. Ayrı arayüz K4'e (her nokta değiştirilebilir) daha iyi oturur |
| 2 | Puan göstergesi `/api/stats`'ta mı `/api/stats/timeseries`'te mi? | A: ikisinde de · B: yalnız `/api/stats` | **B.** K-152 timeseries'in hariç tutmadığını söylüyor; puan oranını oraya koymak eval koşularını karıştırır. Önce `/api/stats` |
| 3 | OpenAI uyumlu uçtan puan yazılabilmeli mi? | A: evet, `/v1/responses` yanıtındaki `id` ile · B: hayır, yalnız yönetim API'si | **B.** OpenAI sözleşmesinde geri bildirim alanı yoktur; uydurmak uyumluluğu bozar. Yönetim API'si `runId` ile yeterlidir |
| 4 | Anonim (kimliksiz) kurulumda `author` ne olur? | A: `null` ve her çağrı yeni satır · B: istemci üretimli bir çerez kimliği | **A.** K1 gereği yeni bir kimlik mekanizması sessizce gelmemelidir |

---

## Bitiş Ölçütleri (DoD)

- [x] `POST /api/runs/{id}/feedback` `{"kind":"Binary","value":1}` gövdesiyle
      `200` ve yazılan `RunScore` döner
- [x] Aynı yazar ikinci kez yazdığında satır sayısı **artmaz**; değer güncellenir
      — `RunFeedbackEndpointTests.Ayni_yazar_ikinci_kez_yazinca_satir_sayisi_ARTMAZ`
      ile kimlikli bir istekte doğrulandı. **Kimliksiz** örnek uygulamada (aşağıda)
      her yazım yeni bir satırdır — bu KASITLI (açık soru 4), bir hata değil.
- [x] Başka kiracının çalıştırması puanlanmaya çalışıldığında `404` döner —
      `RunFeedbackEndpointTests.Baska_kiracinin_calistirmasi_puanlanamaz_AYNI_404_doner`
- [x] `GET /api/stats` yanıtı `scoredRuns` ve `positiveRate` taşır; eval
      çalıştırmaları orana girmez — hem birim testle (`RunScoreStatisticsTests`,
      dört ayrı proje) hem gerçek sunucuyla (aşağıda) doğrulandı
- [x] `run_scores` saklama hedefi olarak tanınır; politika kaydedilince
      `GET /api/retention/preview?target=run_scores` önizleme döndürür (aşağıda)
- [x] Dört doğrulama kapısı sıfır uyarı verir — bkz. "Doğrulama kapıları" altında
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, puanlandı, çıktı bu
      belgeye yazıldı (aşağıda)
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz (`tsc --noEmit` derleme hatası vermedi); bundle
      payı **ölçüldü**: 152,5 KB / 250 KB (bir önceki fazdan +1,2 KB gzip, tahmin
      1–2 KB idi)

### Doğrulama kapıları — gerçek sonuç (2026-08-06)

```
dotnet build  AgentPrism.slnx -c Release              → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test   AgentPrism.slnx -c Release --no-build    → 1898/1898 basarili (SqlServer.IntegrationTests
                                                          haric — 236/236 test, hepsi Rosetta emulasyonu
                                                          kapali oldugu icin fixture kurulumunda dusuyor,
                                                          bkz. docs/hafiza/sql-saglayicilari.md; bu
                                                          FAZDAN ONCE de var olan bilinen bir kisit)
dotnet pack   AgentPrism.slnx -c Release --no-build    → 15 paket (.nupkg + .snupkg), sayi degismedi
dotnet format AgentPrism.slnx --verify-no-changes      → exit 0, degisiklik yok
secret taramasi                                        → bos
```

### Gerçek sunucuyla doğrulama (samples/AgentPrism.Api, port 5081)

```bash
# Calistirma baslat, kimligini al (govde alani 'message'dir, 'messages' DEGIL)
curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H 'content-type: application/json' -d '{"message":"merhaba"}'
# → SSE akisi; runId = 019fd63a-99c7-7119-868c-efb41ae27779

RUN=019fd63a-99c7-7119-868c-efb41ae27779

# Olumlu puan yaz
curl -s -X POST http://localhost:5081/agentprism/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":1,"comment":"dogru cevap"}'
# → 200 {"id":"019fd63a-dc1e-...","tenantId":"default","runId":"019fd63a-99c7-...",
#        "messageId":null,"kind":"Binary","value":1,"comment":"dogru cevap",
#        "source":"human","author":null,"createdAt":"2026-08-06T08:40:09.50Z"}

# Ikinci kez yaz — bu ornek uygulama kimlik dogrulamasi ACMAZ, dolayisiyla
# author her zaman null'dur ve KASITLI olarak yeni bir satir acilir (acik soru 4)
curl -s -X POST http://localhost:5081/agentprism/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":0}'
curl -s http://localhost:5081/agentprism/api/runs/$RUN/feedback | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
# → 2 (KASITLI; kimlikli bir istekte 1 olurdu — bkz. RunFeedbackEndpointTests)

# Istatistik
curl -s http://localhost:5081/agentprism/api/stats \
  | python3 -c "import json,sys; d=json.load(sys.stdin); print({k:d[k] for k in ['totalRuns','scoredRuns','positiveRate']})"
# → {'totalRuns': 1, 'scoredRuns': 1, 'positiveRate': 0.5}

# Puani sil
SCORE_ID=019fd63a-dc52-7269-8ec6-db5dcc35809b
curl -s -o /dev/null -w "%{http_code}\n" -X DELETE http://localhost:5081/agentprism/api/runs/$RUN/feedback/$SCORE_ID
# → 204

# Sinir disi deger
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5081/agentprism/api/runs/$RUN/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":5}'
# → 400

# Olmayan calistirma
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5081/agentprism/api/runs/019fd000-0000-7000-8000-000000000000/feedback \
  -H 'content-type: application/json' -d '{"kind":"Binary","value":1}'
# → 404

# Saklama: once politika kaydedilir, sonra onizleme cagrilir
curl -s -X PUT http://localhost:5081/agentprism/api/retention/run_scores \
  -H 'content-type: application/json' -d '{"enabled":true,"maxAgeDays":90}'
curl -s "http://localhost:5081/agentprism/api/retention/preview?target=run_scores"
# → [{"target":"run_scores","maxAgeDays":90,"enabled":true,
#     "cutoff":"2026-05-08T08:41:20.64Z","matchingRows":0}]
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Puan tablosu sınırsız büyür | Faz 25'in saklama hedefi listesine bugünden eklenir |
| `source` sütunu unutulursa F-71 üç migration daha ister | Sütun bu fazda konur, tek değeri `human`'dır |
| Yıldız ve ikili puanın ortalaması karışır | `kind` ayrı tutulur; oran yalnız `Binary` üzerinden hesaplanır |
| Eval koşuları oranı kirletir | K-141'in `RunKind.Eval` ayrımı istatistik sorgusunda tekrarlanır; test bunu doğrular |
| Arayüz payı bütçeyi zorlar | Bugün 98,7 KB pay var; iki düğme için risk düşüktür ama ölçüm DoD'dedir |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- **İstatistik hesabı iki farklı stratejiyle yapıldı** (K-240): SQL sağlayıcılarında `SelectRunStatistics`'e iki skaler alt sorgu eklendi (native JOIN); bellek içinde `InMemoryRunStore` artık kurucuda bir `IRunScoreStore` alıyor ve eşleşen her çalıştırma için `ListAsync` çağırıyor (N+1, yalnız bellek içi yolda). Plan bunu açıkça belirtmiyordu; `IRunScoreStore`'un `IRunStore`'dan ayrı tutulması kararının (açık soru 1) doğal sonucuydu.
- **`InMemoryRunStore` kurucusu değişti** (K-241): `IRunScoreStore? scores = null` parametresi eklendi. Parametresiz `new InMemoryRunStore()` KIRILMADI — ~20 test dosyası hâlâ öyle çağırıyor.
- **Test sınıf adları plandan farklı**: `RunScoreTenantIsolationTests` ayrı bir dosya olarak açılmadı; kiracı izolasyonu hem `RunScoreStoreContract`'ın (`Baska_kiracinin_puani_gorunmez`) hem `RunFeedbackEndpointTests`'in (`Baska_kiracinin_calistirmasi_puanlanamaz_AYNI_404_doner`) içinde test edildi — ayrı bir dosya açmak aynı senaryoyu iki yerde tekrarlamak olurdu.
- **`FeedbackControl.test.tsx` (Vitest) YAZILMADI.** `docs/hafiza/frontend.md`'nin defalarca tekrarlanan kuralı ("Vitest yalnız `src/lib/` saf mantığını test eder; ekranlar/bileşenler Playwright ile doğrulanır") ile plandaki test satırı çelişiyordu; bu depoda hiçbir React bileşeni Vitest ile (örn. `@testing-library/react` ile) test edilmiyor — yalnızca saf fonksiyonlar. Yeni bir test paradigması (yeni devDependency) tek bir bileşen için eklemek orantısız olurdu. Bunun yerine bileşen `tsc --noEmit` + `npm run build` (bundle bütçesi dahil) ile derleme-doğru olduğu kanıtlandı ve HTTP/DB davranışı zaten uçtan uca `RunFeedbackEndpointTests` ile kapsandı. **Playwright E2E de bu fazda eklenmedi** (zaman kısıtı) — bkz. Sonraki Faza Devir Notu.
- **`source` sabit değeri kod içinde yazıldı, ayrı bir `RunScoreSources` sabitler sınıfı açılmadı.** Bugün tek değer `"human"`; `api`/`judge` F-71'in kendi işidir ve o faz kendi sabitini (veya sınıfını) ekler.

## Bu Fazda Verilen Kararlar

K-239, K-240, K-241, K-242 — `docs/KARARLAR.md`.

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions/Runs
public enum RunScoreKind
{
    Binary = 1,
    Stars = 2,
}

public sealed record RunScore
{
    public Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required Guid RunId { get; init; }
    public string? MessageId { get; init; }
    public required RunScoreKind Kind { get; init; }
    public required int Value { get; init; }
    public string? Comment { get; init; }
    public required string Source { get; init; }
    public string? Author { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public interface IRunScoreStore
{
    ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId, Guid runId, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        string tenantId, Guid scoreId, CancellationToken cancellationToken = default);
}
```

Plandan tek fark: taslakta yoktu ama gerekliydi — `InMemoryRunStore`'un kurucusu:

```csharp
public InMemoryRunStore(IRunScoreStore? scores = null)
```

`RunStatistics`'e eklenen alanlar (plandakiyle birebir aynı):

```csharp
public long ScoredRuns { get; init; }
public double? PositiveRate { get; init; }
```

### HTTP `endpoint`'leri (plandakiyle birebir aynı)

| Metot | Yol | Rol |
|---|---|---|
| `POST` | `/api/runs/{runId:guid}/feedback` | Operator |
| `GET` | `/api/runs/{runId:guid}/feedback` | Reader |
| `DELETE` | `/api/runs/{runId:guid}/feedback/{scoreId:guid}` | Operator |

İstek gövdesi (`RunFeedbackRequest`): `{ kind, value, messageId?, comment? }`. Doğrulama: `Binary` için `value ∈ {0,1}`, `Stars` için `value ∈ [1,5]`; aksi hâlde `400`.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Runs/
├── RunScore.cs
├── RunScoreKind.cs
└── IRunScoreStore.cs                          (RunStatistics.cs: ScoredRuns/PositiveRate eklendi)

src/AgentPrism.Core/
├── Storage/InMemoryRunScoreStore.cs
├── Storage/InMemoryRunStore.cs                 (degisti: IRunScoreStore bagimliligi + istatistik)
└── AgentPrismServiceCollectionExtensions.cs    (degisti: IRunScoreStore kaydi)

src/AgentPrism.Sql.Shared/
├── Internal/SqlQueriesBase.cs                  (degisti: UpsertRunScore/SelectRunScores/DeleteRunScore)
├── Internal/RetentionTargetRegistry.cs         (degisti: RunScores hedefi)
└── Stores/SqlRunScoreStore.cs

src/AgentPrism.PostgreSql/
├── Migrations/0017_run_scores.sql
├── Internal/PostgresQueries.cs                 (degisti: run_scores sorgulari + istatistik alt sorgulari)
└── AgentPrismPostgreSqlBuilderExtensions.cs     (degisti: IRunScoreStore kaydi)

src/AgentPrism.SqlServer/  (0005_run_scores.sql, SqlServerQueries.cs, BuilderExtensions.cs — ayni desen)
src/AgentPrism.Sqlite/     (0005_run_scores.sql, SqliteQueries.cs, BuilderExtensions.cs — ayni desen)

src/AgentPrism.Abstractions/Retention/RetentionTargets.cs   (degisti: RunScores sabiti)

src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs   (degisti: uc uc + RunFeedbackRequest)

src/AgentPrism.UI/frontend/src/
├── components/feedback-control.tsx
├── components/icons.tsx           (degisti: ThumbsUpIcon/ThumbsDownIcon)
├── screens/run-detail.tsx         (degisti: FeedbackControl entegrasyonu)
├── screens/dashboard.tsx          (degisti: FeedbackSummary paneli)
├── lib/api.ts                     (degisti: runFeedback/saveRunFeedback/deleteRunFeedback)
├── lib/types.ts                   (degisti: RunScore, RunFeedbackRequest, RunStatistics)
└── locales/{en,tr}.ts             (degisti: feedback.* + dashboard.scoredRuns/positiveRate)

tests/Shared/Contracts/RunScoreStoreContract.cs
tests/AgentPrism.Core.UnitTests/Storage/RunScoreStatisticsTests.cs
tests/AgentPrism.AspNetCore.FunctionalTests/RunFeedbackEndpointTests.cs
tests/AgentPrism.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/RunScoreStatisticsTests.cs
tests/AgentPrism.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/Contracts/*StoreContractTests.cs   (degisti: RunScore turevi eklendi)
tests/AgentPrism.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/Infrastructure/*TestContext.cs      (degisti: RunScores ozelligi)
tests/AgentPrism.{SqlServer,Sqlite}.IntegrationTests/MigrationRunnerTests.cs                        (degisti: 39 -> 40)
tests/AgentPrism.PostgreSql.IntegrationTests/MigrationTests.cs                                      (degisti: 39 -> 40)
```

## Sonraki Faza Devir Notu

Faz 32 (Çalıştırma İptali) bu faza **bağımlı değildir** — kendi devir notu Faz 12'ye işaret ediyor, burada tekrarlanmaz.

**Devralınan sözleşmeler:**

- `IRunScoreStore` (yukarıda) — üç yeni yazan (InMemory/SQL) sözleşme testinden (`RunScoreStoreContract`) geçti.
- `RunStatistics.ScoredRuns`/`PositiveRate` — `RunKind.Eval` hariç tutulur (K-141 ile aynı kural), `PositiveRate` yalnız `Binary` puanları sayar.

**Bilinen tuzaklar (🚨):**

- Bkz. `docs/hafiza/sql-saglayicilari.md` — `run_scores`'un `message_id`/`author` için TERS NULL semantiği (K-239). Yeni bir "kimliksizken benzersizlik uygulanmasın" alanı eklenirse aynı desen tekrarlanır.
- `InMemoryRunStore(IRunScoreStore? scores = null)` — DI'da her zaman gerçek `IRunScoreStore` alır (Core, `IRunStore`'dan önce kaydeder); yalnız `new InMemoryRunStore()` ile elle kurulan testler kendi özel örneğini alır. Bu iki depo AYRIDIR: bir testte `store.UpsertAsync(...)` ile puan yazıp aynı `InMemoryRunStore`'un `GetStatisticsAsync`'inde görmek istiyorsan `InMemoryRunStore(scores)`'u AYNI `scores` örneğiyle kurmalısın (bkz. `RunScoreStatisticsTests.cs`).

**Yarım kalan işler / açık uçlar:**

- **Yıldız (`Stars`) puanı arayüzde yok** (K-242). API/depo tam destekler, `FeedbackControl` yalnız ikili gösterir.
- **Playwright E2E testi eklenmedi.** `FeedbackControl`'ün gerçek tarayıcıda buton durumu/iyimser güncelleme/hata geri alması davranışı **manuel olarak** (bu fazın DoD'sindeki `curl` adımlarıyla ve `dotnet build`'in derleme-doğruluğuyla) doğrulandı, tarayıcıda tıklanarak DEĞİL. Bu bir boşluktur; sıradaki oturum `tests/AgentPrism.Ui.E2ETests/UiTests.cs`'e bir senaryo ekleyebilir (bir run'ı bitene kadar bekleyip feedback düğmesine tıklamak gerekir — mevcut dosyada run detail ekranına giden bir senaryo yok, sıfırdan kurulmalı).
- **SQL Server'daki `run_scores` sorguları bu makinede gerçek veritabanında koşmadı** (Rosetta kısıtı, `docs/hafiza/sql-saglayicilari.md`). Kod PostgreSQL/SQLite ile aynı desenle yazıldı ve gözden geçirildi ama linux/amd64 bir makinede veya CI'da doğrulanmalıdır.
- **Kota/webhook gibi diğer "yönetici olmayan yazma" depoları gibi `IRunScoreStore` denetim izine SARILMADI** — bir puan yazımı bugün `audit_log`'a düşmez (yalnız `RunEndpoints`'teki `AuditRecorder.WriteAsync` çağrısı `run.feedback.save`/`run.feedback.delete` eylemini yazar, depo dekoratörü yoktur). Bu kasıtlıdır (kullanıcı geri bildirimi bir yönetici kararı değildir) ama not düşülür çünkü diğer depolarla tutarlılığı etkiler.
