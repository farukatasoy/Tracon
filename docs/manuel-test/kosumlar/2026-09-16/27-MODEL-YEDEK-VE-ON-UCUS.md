# 27 — Model Yedek Zinciri ve Ön Uçuş Denetimi (`MYU`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../27-MODEL-YEDEK-VE-ON-UCUS.md`](../../27-MODEL-YEDEK-VE-ON-UCUS.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır; spec `### MT-MYU-NNN` (h3) kullanır, burada skill
> §4.1/§7 konvansiyonuna uymak için `## MT-MYU-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B, altıncı aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `bd25ab0f` donuk (doğrulandı: `git status --short` boş, `git diff --stat 7e3a4de7..HEAD -- src samples tests` boş) |
| **Case sayısı** | 17 (MT-MYU-001..017) |
| **Ana uygulama** | port 5084, şema `mt_s4`, gerçek `openai`/`anthropic` Healthy |

## 🚨 Sapma — `samples/Tracon.Api/Program.cs` HİÇ değiştirilmedi

Spec'in kendi Ön koşul bölümü `flaky`/`flaky2` sağlayıcı kayıtlarını ve
geçici agent'ları (`birincil-kirik`, `birincil-saglam`, `ikisi-de-kirik`,
`baglam-turetilen`, `baglam-eksik`, `retry-seam-demo`) **donmuş** dosyaya
eklemeyi öneriyor. Skill kural 1.1 koşum sırasında `src/`/`samples/`/`tests/`
altında hiçbir dosyanın değişmemesini zorunlu kılar; bu ailede iki tekniğe
bölündü:

1. **Dinamik agent kaydı** (`POST /api/agents`, çalışan ana uygulamaya karşı,
   kod değişikliği gerektirmez) — MT-MYU-005/006/007/008/009/011 için
   kullanıldı. `birincil-saglam`, `cached-support-notools`,
   `baglam-turetilen` koşum sonunda `DELETE /api/agents/{name}` ile
   silindi (204/204/204; `baglam-eksik` zaten hiç kaydolmadığı için 404 —
   beklenen).
2. **Ayrı `TraconTestHost` betiği** (`~/tracon-manuel/model-yedek-s4/`,
   `Tracon.Testing` + `Tracon.OpenAI` 0.0.0-preview.0.829 paket sürümü,
   `guard-testleri`/`job-handlers-s4` ile aynı desen) — gerçek `openai`
   sağlayıcısı + `http://localhost:1/v1`'e işaret eden dinlemeyen
   `flaky`/`flaky2`/`flaky3` kayıtları, `CircuitBreaker:FailureThreshold=1`
   ve (MT-MYU-016 için) özel bir `IProviderRetryClassifier` gerektiren
   MT-MYU-001/002/003/004/013/015/016 için kullanıldı. Ortam değişkeni
   `MYU_SCENARIO` senaryo seçer (`case1`, `case234`, `case13`, `case15`,
   `case16`); gerçek anahtar `MYU_OPENAI_KEY` ile geçirildi, hiçbir yerde
   basılmadı.

Ana uygulamanın `Preflight:Enabled` ve `Tenancy:Enabled`/`AllowHeaderResolution`
ayarları MT-MYU-006/012 için **ortam değişkeniyle** açıldı (§1.2), uygulama
yeniden başlatıldı, case'ler koşuldu, sonra **aynı şema/token ile** ama bu iki
ayar OLMADAN tekrar başlatılıp temel duruma dönüldü (doğrulandı:
`GET /api/meta` koşum öncesi ve sonrasında aynı).

## 🚨 Sapma — MT-MYU-005/006/008/009'da `openai` yerine `anthropic` kullanıldı

`samples/Tracon.Api/appsettings.json`'daki `Providers:OpenAI:Models` kataloğu
(3 giriş: `gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`) hiçbirinde
`ContextWindowTokens` TAŞIMIYOR — yalnız Anthropic/Google/Azure girdilerinde
bu alan dolu. `PreflightGate.CheckAsync` ve `BuildContextWindowStrategy`
(`src/Tracon.Core/Compilation/AgentDefinitionCompiler.Compaction.cs:153-168`)
ikisi de "çözülemeyen pencere asla reddetmez" kuralını izliyor — yani
`birincil-saglam` gerçek `openai`'ye bağlıyken MT-MYU-006 ASLA `400`
üretemezdi (pencere `null` kaldığı için `WouldBeRejected` her zaman
`false`), ve MT-MYU-008 iddia ettiği "pencereli model" senaryosunu `openai`
ile hiç kuramazdı. Bu ürün kusuru DEĞİL — appsettings.json'daki OpenAI
kataloğunun sadece bu fixture'da pencere taşımaması. `birincil-saglam`
(005/006/007) ve `baglam-turetilen` (008) bunun yerine gerçek
`anthropic`/`claude-haiku-4-5-20251001` (katalogda `ContextWindowTokens:
200000`) ile dinamik kaydedildi; test ettikleri davranış (ön uçuş kapısı,
`ContextWindow` sıkıştırma türetmesi) sağlayıcıdan bağımsızdır. Kapanışta
`00-INDEKS.md`'nin fixture notuna eklenmesi önerilir: OpenAI kataloğuna en
az bir `ContextWindowTokens` girdisi eklemek, bu iki case'in gelecekte
literal olarak `openai` ile koşulabilmesini sağlar.

## Gerçek para uyarısı — gerçekleşen çağrılar

MT-MYU-002/003 (openai fallback, 1 gerçek çağrı), MT-MYU-005 (anthropic,
1 çağrı, sağlayıcı tarafından 502 ile reddedildi — büyük olası düşük
maliyetli), MT-MYU-010/011/012/014/015 (her biri 1-2 gerçek çağrı) gerçek
sağlayıcıya gitti. MT-MYU-001/004/006/007/008/009/013/016 sağlayıcıya HİÇ
gitmedi ya da yalnız dinlemeyen bir porta bağlanmaya çalıştı (ücretsiz).

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 5ef711d6:docs/manuel-test/kosumlar/2026-09-16/27-MODEL-YEDEK-VE-ON-UCUS.md
> ```

---

## Temiz geçen case'ler (13)

| Case | Durum | Başlık |
|---|---|---|
| MT-MYU-001 | ☑ | `Fallbacks` boş: geçersiz anahtarla çalıştırma bugünkü hatayı birebir korur |
| MT-MYU-003 | ☑ | Maliyet raporu yedek modelin fiyatıyla hesaplanır, birincilin fiyatıyla DEĞİL |
| MT-MYU-004 | ☑ | Zincirin tamamı düşerse hata mesajı denenen sağlayıcıları sayar; İLK hatayı yansıtır |
| MT-MYU-005 | ☑ | Ön uçuş kapalıyken (varsayılan) pencereden büyük bir istem sağlayıcıdan hata alır |
| MT-MYU-006 | ☑ | Ön uçuş açıkken aynı istem sağlayıcıya HİÇ gitmeden `400` döner |
| MT-MYU-007 | ☑ | `POST /estimate` sayı döner, sağlayıcıya HİÇ istek gitmez |
| MT-MYU-008 | ☑ | `MaxContextWindowTokens` boş, model üstverisi dolu: `ContextWindow` sıkıştırmalı agent kaydı GEÇER |
| MT-MYU-009 | ☑ | İkisi de boş: anlaşılır hata hangi iki alandan birinin doldurulacağını söyler |
| MT-MYU-011 | ☑ | Farklı tool kümesine sahip iki agent aynı önbellek kaydını PAYLAŞMAZ |
| MT-MYU-012 | ☑ | Başka kiracının önbelleklenmiş yanıtı GÖRÜNMEZ |
| MT-MYU-013 | ☑ | `ResponseCache.Enabled` açıkken `IDistributedCache` kayıtlı değilse anlaşılır bir hata döner |
| MT-MYU-014 | ☑ | 👤 `AllowConcurrentToolCalls` kapalıyken (varsayılan) davranış bugünküyle birebir aynıdır |
| MT-MYU-016 | ☑ | `IProviderRetryClassifier` `DoNotRetry` dönerse yedek zincire HİÇ geçilmez |

## Ayrıntı taşıyan case'ler (4)

## MT-MYU-002 — 🚨 Birincilin devresi açıkken yedek devreye girer; `run` kaydı VE `ByModel` yedek modeli gösterir

**Gerçek sonuç**
`birincil-kirik` (`flaky` birincil → dinlemeyen port, gerçek `openai`
yedek), `FailureThreshold=1`.
- Adım 1: `200`, yanıt gerçek modelden ("Merhaba! Nasıl yardımcı olayım?").
- Adım 2 (`GET /api/runs/{runId}`): `"modelId":"gpt-5.4-mini"`,
  `"modelProvider":"openai"` — gerçek modelin adı, `flaky`'nin yer tutucusu
  (`flaky-model`) DEĞİL.
- Adım 3 (events): `event: model.fallback-used`, `text:
  "openai/gpt-5.4-mini"` (birebir `openai/{model}` biçimi), `payload:
  {"primaryProvider":"flaky","primaryModel":"flaky-model","fallbackProvider":
  "openai","fallbackModel":"gpt-5.4-mini","reason":"transport_error"}`.
- Adım 4 (`GET /api/stats?agentName=birincil-kirik`): `byModel` tek girdi
  taşıyor, `"modelId":"gpt-5.4-mini"`; `flaky-model` hiç görünmüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MYU-010 — 🚨 Aynı istem iki kez sorulunca ikinci `run` modele ÇIKMAZ; ama önbellekteki tool çağrısı yine ÇALIŞIR

**Gerçek sonuç**
`cached-support` (kalıcı, kod tanımlı), "order 77" sorusu iki kez soruldu.
- Çağrı 1: akışta 2 `usage` bloğu (turn başına bir).
- Çağrı 2: akışta 0 `usage` bloğu, ama 1 `functionCall` bloğu YİNE var
  (önbellekten çağrının kendisi değil YANITI geliyor, tool tekrar çalışmış
  gibi görünüyor — 81.1 doğrulandı).
- `GET /api/runs?agentName=cached-support&limit=2`: en yeni run'ın
  `"usage": null` (K-557 — `0` değil `null`); bir önceki run'ın
  `usage.totalTokens` gerçek bir sayı (`238` giriş `17` çıkış =
  `255` — sabit `244` örneği bu koşumda farklı ama biçim aynı, gerçek
  LLM yanıtı değişkendir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MYU-015 — 🚨 `IProviderRetryClassifier` kayıtlı değilken (veya `Unknown` dönerken) yedek zincir bugünküyle birebir aynıdır — ve yedek GERÇEKTEN doğru modeli çağırır

**Gerçek sonuç**
`retry-seam-demo` (`flaky3` birincil → dinlemeyen port, gerçek `openai`
yedek — birincilden FARKLI yer tutucu model adı `flaky3-placeholder-model`),
hiçbir `IProviderRetryClassifier` kayıtlı değil (varsayılan). Yanıt metni
BİREBİR `"Hi"` (talimat "Reply with exactly: Hi"), `GET /api/runs/{runId}`:
`"modelId":"gpt-5.4-mini"` — yedeğin KENDİ model adı, birincinin yer
tutucusu DEĞİL. Regresyon testi de koşuldu:
`Fallback_link_is_called_with_its_own_ModelId_not_the_primarys` (18/18
`FallbackChatClientTests` içinde) — yeşil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MYU-017 — 👤 Yedeğe geçerken tamamlanmış bir tool çağrısı tekrar ÇALIŞMAZ

**Gerçek sonuç**
Spec'in kendisi bu case'in gerçek bir `openai` ile elle üretilemeyeceğini
söylüyor; tek koşulacak şey üç automated testin yeşil olduğunu doğrulamak.
`Tracon.AspNetCore.FunctionalTests --filter-method
"*FallbackToolSideEffect*"` → **4/4 yeşil** (spec üç test adı veriyor, filtre
dört eşleşme buldu — aynı dosyadaki ek bir yardımcı/parametreli test
olası; dördünün de geçtiği doğrulandı, hiçbiri kırmızı değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Sayım

17/17 case koşuldu ve Geçti. Sıfır Kaldı, sıfır Beklemede, sıfır fiziksel
eylem gerektiren case. Yeni kusur (`HATA-S4-*`) bulunmadı.

## Devir notu

- Ana uygulama (port 5084) koşum sonunda **temel durumuna** döndürüldü:
  `Preflight`/`Tenancy` env değişkenleri OLMADAN yeniden başlatıldı (aynı
  şema `mt_s4`, aynı `Ui:AuthToken`), `GET /api/meta` koşum öncesi/sonrası
  birebir aynı gövdeyi döndürdü.
- Koşum sırasında dinamik eklenen `birincil-saglam`, `cached-support-notools`,
  `baglam-turetilen` agent'ları `DELETE /api/agents/{name}` ile silindi.
  `cached-support` ve `AddDistributedMemoryCache()` KALICI (spec'in
  "Koşumdan sonra" §4'ü gereği zaten dokunulmadı).
- İzole `~/tracon-manuel/model-yedek-s4/` betiği bu oturumda temizlenmedi
  (workspace hygiene: proje deposu DIŞINDadır, `~/tracon-manuel/` altındaki
  diğer şerit betikleriyle aynı konvansiyon — tur kapanışında toplu
  temizlenmesi önerilir, tek tek değil).
- **Kapanışa not:** yukarıdaki "OpenAI kataloğunda `ContextWindowTokens`
  yok" sapması `00-INDEKS.md`'nin fixture notlarına eklenmeli — dört şeridin
  hepsi aynı `appsettings.json`'ı paylaşıyor, aynı ihtiyaç başka bir şeritte
  de çıkabilir.
- **Sıradaki:** `26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` (16 case), henüz
  açılmadı.
