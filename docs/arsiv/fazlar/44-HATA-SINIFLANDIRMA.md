# Faz 44 — Hata Sınıflandırma ve Arıza Kümeleme

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-55**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli** — iki sütun + bir indeks, üç set (K-178)
> **Public API:** büyüyor — bir arayüz, bir enum, `RunStatistics`'e bir alan. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/44-HATA-SINIFLANDIRMA.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`RunStatistics.FailedRuns` **tek bir sayıdır**. "Son 24 saatte en sık üç hata hangisi?" sorusu bugün cevaplanamıyor; `Failed` çalıştırmaları tek tek açmak gerekiyor. Bir üretim kesintisinde nöbetçi mühendisin ilk sorduğu soru budur. Bu faz o soruyu cevaplanabilir kılar. - **F-55** — hata taksonomisi, normalleştirilmiş arıza parmak izi ve kümeye göre gruplama.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 `GET /api/stats/errors` "son 24 saatte en sık üç hata" sorusunu
      cevaplar — bugün cevaplanamıyor
- [x] Dört `AgentPrismException` alt tipinin **hepsi** kararlı bir `ErrorType`
      yazar (`content_filtered`, `compilation_failed`, `provider_unavailable`,
      `job_retry`)
- [x] Eski ve yeni `error_type` biçimleri **aynı** sınıfa eşlenir
      (`RunErrorLegacyTypeTests` → `Eski_ve_yeni_error_type_bicimleri_ayni_sinifa_eslenir`)
- [x] 🚨 `error_class = NULL` taşıyan eski satırlar `Unknown` kovasında görünür;
      hiçbir uç 500 dönmez (`RunStatisticsLegacyRowTests` — bellek içi +
      PostgreSQL + SQLite'ta geçti)
- [x] Sağlayıcı hatası ve tool hatası **gerçek** bir çalıştırmada doğru sınıfa
      düşer (`samples/AgentPrism.Api`, aşağıdaki "Doğrulama komutları" çıktısı).
      İçerik filtresi ve kota aşımı gerçek uçtan uca **değil**, otomatik testle
      doğrulandı — gerekçe "Plandan Sapmalar"da
- [x] Parmak izi kümeleme **ölçüldü**: bkz. "Bu Fazda Verilen Kararlar" —
      `Unknown` oranı örnek uygulamada **%0** (2/2 gerçek hata doğru sınıfa düştü;
      biri düzeltme gerektirdi, bkz. K-296)
- [x] Başarılı çalıştırmada sınıflandırıcı çağrılmaz
      (`ErrorClassifierHotPathTests` → `Basarili_calistirmada_hata_siniflandirici_hic_cagrilmaz`)
- [x] Sözleşme testleri bellek içi + PostgreSQL + SQLite'ta geçti (821 ve 424
      test, sırasıyla). SQL Server bu ortamda Docker/Rosetta kısıtı yüzünden
      **ölçülmedi** (`docs/hafiza/sql-saglayicilari.md`'deki bilinen kısıt)
- [x] Migration PostgreSQL (`0021`) ve SQLite (`0009`) setlerinde gerçekten
      uygulandı ve test edildi; SQL Server (`0009`) sözdizimi PostgreSQL'in
      birebir aynısı (filtreli indeks) ama bu ortamda **çalıştırılamadı**
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı **156,6 KB gzip / 250 KB bütçe**
      (2026-08-07, tam çözüm derlemesinde ölçüldü)

### Doğrulama komutları

> 🚨 Plan taslağı `POST .../run` gövdesini `{"messages":[{"role":"user","text":...}]}`
> olarak varsaymıştı; gerçek sözleşme `AgentRunRequest.Message` (tekil, düz
> `string`) alanıdır. Aşağıdaki komutlar `samples/AgentPrism.Api`'ye karşı
> (varsayılan model sağlayıcısı `EchoModelProvider`, bellek içi depo) 2026-08-07
> tarihinde gerçekten çalıştırıldı.

```bash
# Gercek bir saglayici hatasi: gecersiz model adi, OpenAI 404 dondurur
curl -s -X POST http://localhost:5080/agentprism/api/agents \
  -H 'content-type: application/json' \
  -d '{"name":"kirik-saglayici-test","instructions":"Kisa yanit ver.",
       "model":{"provider":"openai","model":"gpt-olmayan-model-xyz"}}'
curl -s -X POST http://localhost:5080/agentprism/api/agents/kirik-saglayici-test/run \
  -H 'content-type: application/json' -d '{"message":"merhaba"}'

# Gercek bir tool hatasi: alt agent onay istegi yasagi (yonlendirici -> support -> cancel_order)
curl -s -X POST http://localhost:5080/agentprism/api/agents/yonlendirici/run \
  -H 'content-type: application/json' \
  -d '{"message":"ORD-1001 siparisini iptal et, destek ekibine yonlendir ve cancel_order tool unu cagirmasini soyle."}'

# Hata kirilimi
curl -s "http://localhost:5080/agentprism/api/stats/errors?hours=24" | python3 -m json.tool
```

Gerçek çıktı (kısaltıldı):

```json
[
  {
    "class": "ProviderError",
    "totalRuns": 1,
    "topClusters": [{
      "fingerprint": "0e3f1161f3710a54675fb4169bd0c685582892dfb104f911890bc331f28b6050",
      "count": 1,
      "sampleMessage": "HTTP 404 (invalid_request_error: model_not_found)\n\nThe model `gpt-olmayan-model-xyz` does not exist or you do not have access to it.",
      "sampleRunId": "019fdb28-c018-72f9-8066-21abc7df7153",
      "lastSeenAt": "2026-08-07T07:38:28.792212+00:00"
    }]
  },
  {
    "class": "ToolError",
    "totalRuns": 1,
    "topClusters": [{
      "fingerprint": "103d27693a23b99864af7c4db1fa77a41825587b46a1d3e83fdc20f561e8e581",
      "count": 1,
      "sampleMessage": "Alt calistirma 'cancel_order' tool'u icin kullanici onayi istedi. Alt agent onay isteyemez: ...",
      "sampleRunId": "019fdb28-c6d7-7f03-aa2c-4ee050367eff",
      "lastSeenAt": "2026-08-07T07:38:30.488511+00:00"
    }]
  }
]
```

`Unknown` oranı bu oturumda **0/2 = %0**. İlk denemede `ProviderError` yerine
`Unknown` çıkmıştı (OpenAI SDK'sının `ClientResultException` fırlattığı
görülmemişti) — sınıflandırıcı düzeltildi (K-296), yeniden çalıştırıldı ve
yukarıdaki sonuç alındı. Gösterge panelinde "Error breakdown" bölümü ekran
görüntüsüyle doğrulandı: her iki sınıf, örnek mesajı ve "N sec. ago" göreli
zamanıyla göründü.

---

## Plandan Sapmalar

- **`quota_exceeded` uçtan uca gösterilemedi.** Aday listesi bunu dört zorunlu
  örnekten biri sayıyordu; kod incelemesi `QuotaGate`'in (Faz 21) bir
  çalıştırma `RunRecordingAgent`'a hiç ulaşmadan HTTP katmanında `429`
  döndürdüğünü ortaya çıkardı (K-162). Bu YAPISAL bir engeldir, ortam kısıtı
  değil — sınıf taksonomide kalır, birim testiyle doğrulandı (K-297).
- **`content_filtered` gerçek bir Gemini çağrısıyla tetiklenemedi.** En katı
  güvenlik eşiğiyle bile ("gemini-kati-filtre") ölçüm sırasında mesaj
  filtrelenmedi; kasıtlı olarak gerçekten zararlı içerik denenmedi (üçüncü
  taraf bir servise karşı böyle bir istek uygun değildir). Sınıf,
  `RunRecordingAgentTests`'in mevcut `FakeChatClient` + `ChatFinishReason.ContentFilter`
  testleriyle (Faz 26'dan beri var) doğrulanmış durumda kalıyor.
- **Plan taslağının `POST .../run` gövde örneği yanlıştı**
  (`{"messages":[...]}` değil `{"message": "..."}`) — doğrulama komutları
  düzeltildi.
- **Sınıflandırıcının tip deseni ölçüm sırasında genişletildi.** Plan yalnız
  `HttpRequestException` ailesini öngörmüştü; gerçek bir OpenAI çağrısı
  `System.ClientModel.ClientResultException` fırlattı ve ilk denemede
  `Unknown`'a düştü. Bu, "gerçek bir çalıştırmayı çalıştır" adımının tam
  amacıdır — birim testi bu boşluğu göremezdi (K-296).
- **SQL Server sözleşme testleri bu ortamda hiç çalıştırılamadı.**
  `mcr.microsoft.com/mssql/server` Apple Silicon'da Rosetta emülasyonu
  gerektirir (`docs/hafiza/sql-saglayicilari.md`, önceden bilinen kısıt).
  Sorgu metni PostgreSQL ile karakter karakter aynı desende yazıldı
  (filtreli indeks sözdizimi K-178'in devir notundan zaten biliniyordu) ama
  gerçek bir SQL Server'da **ölçülmedi**.
- **Tam çözüm `dotnet test`i bu ortamda tamamlanamadı** (`AgentPrism.Templates.Tests`
  13+ dakika boyunca hiç test başlatmadan takıldı — muhtemelen ağ/şablon
  restore gecikmesi, bu fazla ilgisiz). Doğrulama bunun yerine etkilenen
  projeler tek tek çalıştırılarak yapıldı: `AgentPrism.Core.UnitTests` (615),
  `AgentPrism.PostgreSql.IntegrationTests` (bellek içi + PostgreSQL, 821),
  `AgentPrism.Sqlite.IntegrationTests` (424), `AgentPrism.AspNetCore.FunctionalTests`
  (348, OpenAPI anlık görüntüsü yenilendi), arayüz `vitest`+`tsc` (141 test).

## Bu Fazda Verilen Kararlar

K-293 · K-294 · K-295 · K-296 · K-297 · K-298 · K-299 — tam gerekçeleri
`docs/KARARLAR.md`'de. Özet:

| Karar | Konu |
|---|---|
| K-293 | `RunError` sınıf/parmak izini doğrudan taşır (ayrı arama tablosu yok) |
| K-294 | Sınıflandırma tek noktada, `RunRecordingAgent.CompleteAsync` içinde |
| K-295 | `ByErrorClass` ayrı depo metodu değil, `GetStatisticsAsync`'in parçası |
| K-296 | `ClientResultException` eksikti — gerçek çalıştırma bunu ortaya çıkardı |
| K-297 | `quota_exceeded` yapısal olarak ulaşılamaz (`QuotaGate`, K-162) |
| K-298 | Parmak izi tırnak içi metni SİLMEZ (Açık Soru 3 → C) |
| K-299 | Kümeleme pencere fonksiyonlarıyla — kod tabanında ilk kullanım |

**Ölçülen `Unknown` oranı:** örnek uygulamada üretilen 2 gerçek hatanın
2'si de (düzeltmeden sonra) doğru sınıfa düştü — **%0 Unknown**. Küme sayısı:
2 sınıf, sınıf başına 1 küme (oturumda tekrar eden arıza yok).

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**

- `IRunErrorClassifier.Classify(RunError)` — `TryAddSingleton` ile kayıtlı,
  `RunRecordingAgent.CompleteAsync` içinde tek noktadan çağrılır. Kendi
  sınıflandırıcısını yazan bir tüketici `DefaultRunErrorClassifier`'ı
  sarmalayabilir (K4).
- `RunStatistics.ByErrorClass` — `GetStatisticsAsync`'in parçası, ayrı bir
  sorgu yolu değil. Yeni bir kırılım eklerken bu deseni izleyin: `ByAgent`/
  `ByModel`/`ByVersion`/`ByErrorClass` hepsi AYNI çağrının kırılımlarıdır.
- `GET /api/stats/errors?hours=` — `statistics.ByErrorClass`'ın ince bir
  dilimi; yeni bir depo metodu AÇMAZ.

**Bilinen tuzaklar (🚨):**

- **`quota_exceeded` otomatik sınıflandırıcı için asla gerçek bir `RunError`
  üretmez** (K-297) — `QuotaGate` çalıştırma başlamadan `429` döner. F-74'ün
  "eşik kuralı" bu sınıfı KULLANAMAZ; kanarya karar mantığı bu sınıfı hiç
  görmeyecek şekilde tasarlanmalı veya `QuotaEnforcer`'a ayrı bir kanca
  eklenmelidir.
- **SDK istisna adları yalnız gerçek bir sağlayıcı çağrısıyla ortaya çıkar**
  (K-296) — yeni bir birinci sınıf sağlayıcı eklerken `DefaultRunErrorClassifier.ProviderErrorTypePattern`'i
  o SDK'nın gerçek istisna adıyla (birim testi değil, `samples/AgentPrism.Api`
  üzerinden) doğrulayın.
- **Pencere fonksiyonu deseni bu kod tabanında YENİDİR** (K-299) — dördüncü
  bir SQL sağlayıcısı eklenirse `ROW_NUMBER()`/`COUNT() OVER` desteği önce
  ölçülmelidir.
- **SQL Server bu ortamda hiç test edilemedi** — Apple Silicon + Docker
  Desktop Rosetta kısıtı (`docs/hafiza/sql-saglayicilari.md`). `0009_error_classification.sql`
  gerçek bir SQL Server'da bir kez çalıştırılıp doğrulanmalıdır.

**Bu fazın üstüne kurulan iki kalem:**

1. Aday listesindeki **F-74** (kanarya yayını ve otomatik geri alma) — eşik
   kuralı `error_class` üzerine kurulur. `quota_exceeded`'ın yukarıdaki
   tuzağı yüzünden "durdurulabilir" sınıf kümesi muhtemelen `ProviderError`,
   `ProviderUnavailable`, `Timeout`, `RateLimited` ile sınırlı tutulmalıdır —
   `ToolError`/`CompilationFailed` genelde kod/tanım hatasıdır, trafik artışı
   onları durdurmaz.
2. [Faz 21](21-KOTA-VE-OLAY-YAYINI.md)'in webhook'u "bu küme %5'i aştı"
   kuralıyla anlam kazanır. `RunErrorCluster.Fingerprint` (64 karakterlik
   SHA-256 onaltılık dize) webhook yüküne eklenmeye hazırdır; `SampleMessage`
   serbest metin olduğu için `secret` filtresinden geçirilmeden webhook'a
   YAZILMAMALIDIR (K-081'in aynı dersi).

**Yarım kalan iş yok** — dört doğrulama kapısı ve DoD'nin tamamı bu fazda
kapatıldı (SQL Server ölçümü hariç, yukarıda not edildi).
