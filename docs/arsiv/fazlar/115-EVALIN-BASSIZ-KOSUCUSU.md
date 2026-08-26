# Faz 115 — Eval'in Başsız Koşucusu

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-168**
> **Önkoşul:** Faz 18 (eval altyapısı) ve Faz 83 (tipli istemci ve CLI) — ikisi de arşivde; yalnız aşağıdaki grep'lerle okunur
> **Paketler:** `AgentPrism.Cli` (tek paket)
> **Yeni paket:** Yok — `AgentPrism.Client` referansı **zaten var** · **Migration:** Yok
> **Public API:** `PublicAPI.*.txt` **değişmiyor** (`AgentPrismPublicApiTrackingEnabled=false`, [Cli.csproj:18](../../../src/AgentPrism.Cli/AgentPrism.Cli.csproj)). Fakat komut adı, bayraklar ve **exit code'lar sevk edilen bir sözleşmedir** ve sonradan ucuz değişmez
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/cli.md`, `capabilities.md`
> · sevk edilen: `src/AgentPrism.Cli/README.md`, `Program.cs`'in `PrintHelp()` metni, `Cli.csproj` `<Description>`
> **Manuel test alanı:** `docs/manuel-test/34-ISTEMCI-VE-CLI.md` · `docs/manuel-test/17-EVAL-VE-DENEYLER.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 3f7dc97:docs/arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md
> ```
>
> Damıtıldı 2026-08-27 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in eval çekirdeği tamdır ve HTTP'den tetiklenebilir, fakat onu koşup **exit code üreten** bir yol yoktur. Kalite ölçümü arayüzden elle tetiklenmeye bağlıdır. Bu faz `agentprism eval` komutunu ekler: suite'i tetikler, bitene kadar yoklar, tüketicinin verdiği eşiğe göre exit code üretir. - **F-168** — agent kalitesi, kod kalitesiyle aynı CI adımından geçebilir.

## Bitiş Ölçütleri (DoD)

- [x] Eşiği geçen bir suite için `agentprism eval … --min-pass-rate 1.0` → exit `0`
- [x] Bir case'i düşen suite için aynı komut → exit **`3`**, çıktıda düşen case **kimliği** var (`EvalCaseResult` bir isim taşımaz — bkz. Plandan Sapmalar)
- [x] İki eşik birlikte verildiğinde **ikisi birden** sağlanmadıkça `3` döner
- [x] `Failed`/`Cancelled` biten eval → exit `2` (asla `3` değil)
- [x] `--timeout` dolduğunda komut çıkar → exit `2`; asılı kalmaz
- [x] Boş suite tetiklenemez (`400`, hiç run oluşmaz) → exit `2`; `--min-pass-rate`'in kendi `Total == 0` koruması yine de kodda kalır (bkz. Plandan Sapmalar)
- [x] Yalnız `EvalsRead` scope'lu anahtarda hata metni **`RunsWrite`**'ı adıyla söyler
- [x] Sunucu hata gövdesi hiçbir çıktıya sızmaz (`CliSecretRedactionTests` eval komutunu kapsar)
- [x] Üç mevcut komutun `0`/`1`/`2` anlamı **değişmedi**
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis` — tam koşum, tüm projeler yeşil)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü (`python3 scripts/kapi.py tarama` → `✅ temiz`)
- [x] Manuel kabul case'leri `docs/manuel-test/34-ISTEMCI-VE-CLI.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (ikisi de düzeltildi — bkz. Denetim Bulguları)
- [x] `docs-site/guides/cli.md` yeni komutu ve **exit code tablosunu** yazar; `npm run build` + `check-links.mjs` temiz
- [x] `src/AgentPrism.Cli/README.md`, `PrintHelp()` ve `<Description>` üçü de tutarlı

### Gerçek `run` kanıtı — `samples/AgentPrism.Api`'ye karşı

Sunucu gerçekten çalıştırıldı (`support` agent'ı, gerçek OpenAI çağrısı —
`dotnet user-secrets`'ta bu makineye özgü bir manuel-test token'ı ve
sağlayıcı anahtarı zaten kuruluydu). Bir suite/case oluşturuldu, sonra CLI
gerçek HTTP üzerinden koşturuldu:

```
$ agentprism eval --url http://localhost:5081/agentprism --suite demo-suite \
    --token <token> --min-pass-rate 1.0 --poll-interval 2 --timeout 60
Completed: 1/1 passed in 8,1 s.
exit=0
```

`--json` ile aynı koşum (gerçek model kimliği ve token sayıları görülür —
`secret` içermez):

```json
{
  "run": {
    "status": "Completed", "total": 1, "passed": 1, "failed": 0,
    "agentVersion": 1, "modelId": "gpt-5.4-mini",
    "inputTokens": 234, "outputTokens": 38
  },
  "results": [{ "passed": true, "scores": [{"name": "non_empty", "passed": true}] }]
}
exit=0
```

### Doğrulama komutları

```bash
# Kapı kapanıyor mu
agentprism eval --url http://localhost:5081/agentprism --suite checkout --min-pass-rate 1.0
echo "exit=$?"   # beklenen: 3 (bir case düşükse)

# Kapı yokken hiçbir şey kırmıyor
agentprism eval --url http://localhost:5081/agentprism --suite checkout
echo "exit=$?"   # beklenen: 0
```

---

## Plandan Sapmalar

1. **Boş suite `--min-pass-rate` ile `3` DÖNEMEZ; `2` döner.** Planın Açık
   Soru 3'ü ve DoD satırı, `Total == 0` olan bir **run**'ın var olacağını
   varsayıyordu. Gerçek sunucu davranışı ölçüldüğünde
   ([`EvalEndpoints.cs:479-484`](../../../src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs))
   `POST /api/evals/{name}/run` sıfır case'li bir suite'i `400 "has no
   cases"` ile **trigger anında** reddediyor — hiçbir run kaydı hiç
   oluşmuyor. Dolayısıyla `Total == 0` bir `Completed` run bugünkü kod
   yollarından **hiçbirinde** üretilemez; CLI bu durumu doğal olarak `2`
   ("koşamadı") ile karşılar, `3` ("koştu ama eşiği kaçırdı") değil —
   ayrımın kendi felsefesiyle ("`2` altyapı sorunudur", 115.4) tutarlı.
   `PassesThreshold`'un `Total > 0` koruması yine de kodda kalır: savunma
   amaçlı, gelecekte `EvalRunDetailResponse`'u başka bir yoldan üreten bir
   sunucu sürümüne karşı. Kanıt:
   `EvalCommandTests.A_suite_with_no_cases_cannot_be_run_at_all`.
2. **"Düşen case adı" değil "düşen case kimliği".** Plan metni (115.5, DoD)
   "case adı" diyordu; `EvalCaseResult` **kasıtlı olarak** bir isim taşımaz
   (yalnız `CaseId`, bkz. `EvalCaseResult.cs`'in kendi XML sözü — bir case
   sonradan değişse/silinse bile geçmiş sonucun anlaşılır kalması için).
   `agentprism eval`'in çıktısı bu yüzden `FAILED case <Guid>` yazar,
   okunabilir bir metin değil. DoD ve doküman metni buna göre düzeltildi;
   davranış değişmedi, yalnız vaat doğru kelimeye çekildi.
3. **İki önceden var olan, faz dışı kusur bulundu ve düzeltildi** (kullanıcı
   talimatı: "konuyla alakasız bug/defect'lerle karşılaşırsan onları da
   çöz"). İkisi de `AgentPrismTriggerEvalRunAsync`/`AgentPrismGetEvalRunAsync`'in
   bu fazda İLK KEZ gerçek veriyle çağrılmasıyla ortaya çıktı — daha önce
   hiçbir test bu iki metodu gerçek bir sunucuya karşı koşmamıştı
   (`AgentPrismTestHost`'un in-memory `TestServer`'ı ham `HttpClient`
   kullanıyor, üretilmiş istemciyi değil):
   - `scripts/generate-client-json-context.py`'deki bir regex, `Type? body =
     null` biçimindeki OPSİYONEL istek gövdesi parametrelerini (9 metot,
     `EvalRunTriggerRequest` dahil) JsonSerializerContext'ten atlıyordu —
     her çağrı `NotSupportedException` fırlatıyordu, `body` verilse de
     verilmese de (tip statik olarak çözülüyor). Düzeltme:
     `BODY_PARAM_RE`'ye nullable `?` ve `= null` varsayılanı toleransı
     eklendi.
   - `scripts/nswag-postprocess-client.py`: kendi `[JsonConverter]`'ı olan
     bir değer tipi (`System.Text.Json.JsonElement`, `Microsoft.Extensions.AI.ChatRole`)
     için NSwag boş `{}` şema üretiyor ve bu şemayı tipin KISA ADIYLA bir
     POCO sınıfına çeviriyordu — bu sınıf AYNI ad alanında GERÇEK tipi
     gölgeliyordu (`AgentPrism.Client.Generated.JsonElement` ≠
     `System.Text.Json.JsonElement`). 16 alanın TÜMÜ (`EvalCaseResult.Scores`
     dahil) etkileniyordu; tel üzerindeki değer bir JSON NESNESİ değilse
     (`Scores` bir dizidir) her çağrı `JsonException` fırlatıyordu. Düzeltme:
     `COLLIDING_ANY_TYPES` tablosu bogus sınıfı siler, referansları gerçek
     tipe (`JsonElement`) ya da — `AgentPrism.Client`'ın bilerek bağımlı
     OLMADIĞI bir paketin tipiyse (`ChatRole`) — o tipin gerçek tel biçimine
     (`string`) nitelendirir. Ayrıntı ve sınıf taraması sonucu:
     `docs/hafiza/paketleme-ve-dagitim.md`, `docs/KARARLAR.md` K-633,
     `scripts/nswag_postprocess_client_test.py`.

## Bu Fazda Verilen Kararlar

- **K-633** — `AgentPrism.Client`'ın üretilmiş `JsonElement`/`ChatRole`-tipli
  alanları geriye dönük uyumsuz biçimde düzeltildi (yukarıdaki sapma 3);
  gerekçe ve yeniden açılma koşulu `docs/KARARLAR.md`'dedir.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, `docs/115-EVALIN-BASSIZ-KOSUCUSU.md` +
`git diff` okuyarak) iki 🔴 ve üç 🟡 bulgu üretti; sıfır 🔴 kaldı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `capabilities.md`'nin CLI satırı `eval`'i saymıyordu | Düzeltildi |
| 2 | 🔴 | DoD/doküman metni, sunucunun gerçek 400-reddi ile çelişen "boş suite `3` döner" iddiasını taşıyordu | Düzeltildi (Plandan Sapmalar 1) — DoD, README, cli.md, EvalCommandTests hepsi gerçek davranışa çekildi |
| 3 | 🟡 | `EvalRunStatus.Failed`/`Cancelled` → exit `2` dalını tetikleyen test yoktu (fazın kendi en kritik risk maddesi) | Düzeltildi — `A_run_that_ends_Failed_exits_2_never_3` eklendi (suite'in agent'ı yok, `EvalJobHandler` suite'i `Failed` ile bitirir) |
| 4 | 🟡 | "case adı" vaadi gerçek sözleşmeyle (yalnız `CaseId`) uyuşmuyordu; testin adı "isimlendirir" diyordu ama yalnız sabit alt dize arıyordu | Düzeltildi (Plandan Sapmalar 2) — metin "case kimliği"ne çekildi, test artık gerçek `Guid` değerini iddia ediyor |
| 5 | 🟡 | `AgentPrism.Client`'ın üretilmiş tip değişikliği (kusur düzeltmesi) `PublicApiTrackingEnabled=false` olduğu için hiçbir analyzer'dan geçmiyordu; karar defteri kaydı yoktu | Düzeltildi — K-633 eklendi |
| 6 | 🟢 | `IMcpToolRefresher` (bir DI parametresi, istek gövdesi değil) opsiyonel-body regex düzeltmesinden yan etkiyle etkilendi — zararsız (`AgentPrismRefreshMcpToolsAsync` artık çöküyor DEĞİL, aksine düzeliyor) | Devredilmedi — `docs/hafiza/paketleme-ve-dagitim.md`'nin genel sınıf-taraması komutu bunu bir sonraki `nswag` rejenerasyonunda zaten kapsıyor |

## Sonraki Faza Devir Notu

- `agentprism eval` artık gerçek, çift-katmanlı bir kusur sınıfını
  (`AgentPrism.Client`'ın üretilmiş dosyalarındaki NSwag/NJsonSchema
  tuzakları) kapsayan bir regresyon setiyle korunuyor
  (`scripts/nswag_postprocess_client_test.py`,
  `docs/hafiza/paketleme-ve-dagitim.md`). Bir sonraki `dotnet nswag run`
  öncesi, yeni bir "opak değer tipi" (kendi `[JsonConverter]`'ı olan, boş
  şema üreten) eklenirse aynı sınıf taraması komutu (hafıza notunda) tekrar
  koşulmalı.
- `AgentPrism.Client`'ın üretilmiş dosyalarının **hiçbiri** artık `AgentPrism.Client.Generated`
  ad alanında BCL/MEAI tipiyle aynı kısa adı taşımıyor — bu, üretilmiş
  istemcinin manuel post-processing'e bağımlılığını artıran bir örnek daha;
  `AgentPrismClientJsonContext.g.cs`'in üretim SIRASI hâlâ kritik
  (`nswag-postprocess-client.py` HER ZAMAN `generate-client-json-context.py`'den
  ÖNCE koşmalı — aksi hâlde context bogus sınıfı kaydeder).
- Faz 116 (Performans Tahsis Kapısı) bu fazla dosya/kod düzeyinde
  kesişmiyor; `agentprism eval`'in kendi eşik felsefesi (115.3) F-67'nin
  "performans kapısı" tasarımına referans niteliğinde olabilir ama F-67
  ayrı bir eşik ailesi ister (115.6'da kapsam dışı bırakıldı).
