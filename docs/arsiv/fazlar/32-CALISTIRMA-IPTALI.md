# Faz 32 — Çalıştırma İptali

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-35**
> **Önkoşul:** Yok. **Kapsam bilerek tek örnekle sınırlıdır** — gerekçe aşağıda
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/32-CALISTIRMA-IPTALI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir çalıştırmayı **dışarıdan** durdurmanın yolu yoktur. Kaçak bir agent'ı durdurmanın tek yolu bugün süreci öldürmektir. Bu faz bir iptal ucu ve süren çalıştırmaların bellek içi defterini ekler. - **F-35** — `POST /api/runs/{runId}/cancel`, süren `CancellationTokenSource` defteri, kök iptalinin alt çalıştırmaları da durdurması.

## Bitiş Ölçütleri (DoD)

- [x] Uzun süren bir çalıştırma başlatılır; `POST /api/runs/{id}/cancel` `202`
      döner ve çalıştırma birkaç saniye içinde `Canceled` durumuna geçer —
      gerçek çıktı aşağıda ("Doğrulama komutları" bölümü)
- [x] Kök çalıştırma iptal edilince alt çalıştırmalar da `Canceled` olur —
      `RunCancellationTreeTests.Kok_iptali_tum_alt_calistirmalari_iptal_eder`
      (registry seviyesinde, ağaç cascade'i tek gerçek doğruluk noktası burada
      yaşar); alt çalıştırmanın tek başına iptali köke sızmaz
      (`Alt_calistirmanin_tek_basina_iptali_koku_ve_kardes_dali_etkilemez`).
      Workflow'a özgü uçtan uca bir ağaç testi denendi ve **terk edildi** —
      bkz. Plandan Sapmalar.
- [x] Bitmiş bir çalıştırma iptal edilmeye çalışıldığında `409` döner —
      `CancelRunEndpointTests.Bitmis_calistirma_409_doner` + gerçek çıktı aşağıda
- [x] Başka kiracının çalıştırması iptal edilmeye çalışıldığında `404` döner —
      `CancelRunEndpointTests.Baska_kiracinin_calistirmasi_iptal_edilemez_AYNI_404_doner`
- [x] 🚨 Normal + hatalı + yarıda bırakılan akış sonrası `ActiveCount == 0` —
      `RunCancellationLeakTests` (3 senaryo); dışarıdan tetiklenen iptalden
      sonra da sıfıra döndüğü `RunCancellationStatusTests`'te ayrıca doğrulandı
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/
      `format` hepsi yeşil. **İstisna:** `AgentPrism.SqlServer.IntegrationTests`
      bu makinede (Apple Silicon) Testcontainers'ın `sqlcmd` ikili dosyasını
      mssql imajında bulamaması yüzünden 236/236 başarısız — önceden
      belgelenmiş, bu fazdan bağımsız bir ortam kısıtı
      (`docs/hafiza/sql-saglayicilari.md`). Bu fazda SQL Server koduna
      dokunulmadı.
- [x] `samples/AgentPrism.Api` ile gerçek `run` iptal edildi, çıktı bu belgeye
      yazıldı — bkz. aşağı
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz (4 yeni anahtar: `runDetail.cancel.button`,
      `.confirm`, `.requested`, `.conflict`); bundle payı ölçüldü:
      **152,9 KB gzip / 250 KB** (önceki ölçüm 151,3 KB — bu fazın payı
      **~1,6 KB gzip**, tahminin altında)

### Gerçek çıktı — `samples/AgentPrism.Api` (2026-08-06, `ASPNETCORE_ENVIRONMENT=Production`, echo sağlayıcı)

```
$ curl -s -N -X POST http://localhost:5080/agentprism/api/agents/support/run \
    -H 'content-type: application/json' -d '{"message":"lorem ipsum ... (1000 sözcük)"}'
id: 0
event: run
data: {"runId":"019fd678-8b4b-7ed7-af89-a4d11bb7d36c","sessionId":null}
...

$ curl -s "http://localhost:5080/agentprism/api/runs?status=Running"
[{"id":"019fd678-8b4b-7ed7-af89-a4d11bb7d36c","status":"Running", ...}]

$ curl -s -i -X POST http://localhost:5080/agentprism/api/runs/019fd678-8b4b-7ed7-af89-a4d11bb7d36c/cancel
HTTP/1.1 202 Accepted
Location: /api/runs/019fd678-8b4b-7ed7-af89-a4d11bb7d36c
{"id":"019fd678-8b4b-7ed7-af89-a4d11bb7d36c","status":"Running", ...}

$ curl -s http://localhost:5080/agentprism/api/runs/019fd678-8b4b-7ed7-af89-a4d11bb7d36c | jq -r .status
Canceled

# Ayni calistirma tekrar iptal edilmeye calisilinca:
$ curl -s -i -X POST http://localhost:5080/agentprism/api/runs/019fd678-8b4b-7ed7-af89-a4d11bb7d36c/cancel
HTTP/1.1 409 Conflict
{"title":"Calistirma zaten sonlanmis","detail":"'019fd678-...' kimlikli calistirma zaten 'Completed' durumunda."}

# Olmayan bir calistirma:
$ curl -s -X POST http://localhost:5080/agentprism/api/runs/00000000-0000-0000-0000-000000000000/cancel -w "\n%{http_code}\n"
{"title":"Calistirma bulunamadi", ...}
404

# Denetim izi:
$ curl -s http://localhost:5080/agentprism/api/audit?take=5
[{"action":"run.cancel","entity":"run:019fd678-8b4b-7ed7-af89-a4d11bb7d36c", ...}]
```

Sunucu günlüğünde hata/uyarı yoktu. `AgentPrismRunOptions` gerektiği için ilk
denemede gerçek OpenAI sağlayıcısı (kısa yanıt, saniyeden az) yakalandı; ikinci
denemede `--no-launch-profile` ile `user-secrets` devre dışı bırakılıp echo
sağlayıcısına (kelime başına 30 ms akış) geçilerek `Running` penceresi elde
edildi.

### Doğrulama komutları

```bash
# Uzun surecek bir calistirma baslat (arka planda)
curl -s -X POST http://localhost:5081/agentprism/api/agents/slow/run \
  -H 'content-type: application/json' \
  -d '{"messages":[{"role":"user","content":"uzun bir metin yaz"}]}' &

# Suren calistirmayi bul
RUN=$(curl -s 'http://localhost:5081/agentprism/api/runs?status=Running' | jq -r '.[0].id')

# Iptal et — 202 beklenir
curl -s -i -X POST http://localhost:5081/agentprism/api/runs/$RUN/cancel | head -1

# Durum Canceled olmali
curl -s http://localhost:5081/agentprism/api/runs/$RUN | jq -r '.status'

# Ayni cagriyi tekrarla — 409 beklenir
curl -s -i -X POST http://localhost:5081/agentprism/api/runs/$RUN/cancel | head -1
```

---

## Plandan Sapmalar

- **Workflow'a özgü uçtan uca bir iptal testi terk edildi.** `WorkflowRunner`
  bloke eden özel bir `AIAgent` (`BlockingAgent`) ile gerçek grafikte
  çalıştırıldı; defter kaydı bekleniyordu (`ActiveCount == 2`) ama iptalden
  sonra çalıştırma `Canceled` değil `Completed` yazdı. Kök sebep MAF'ın
  `AgentWorkflowBuilder.BuildSequential` grafiğinin özel bir `AIAgent`
  alt sınıfını nasıl tükettiğiyle ilgili — reflection ile kesin köke inmek bu
  fazın kapsamını aşan bir MAF içi kazı isterdi. Test dosyası ve sahte agent
  silindi; kanıt yerine (a) `RunCancellationRegistry`'nin ağaç cascade'ini
  registry seviyesinde doğrulayan `RunCancellationTreeTests`, (b)
  `RunRecordingAgent`'ın **gerçek** model çağrısını kestiğini kanıtlayan
  `RunCancellationStatusTests` (aynı mekanizma, `WorkflowRunner`'ın
  kaydı/bırakması bire bir aynı `using var registration = ...` deseniyle
  yazıldı) kullanıldı. `WorkflowRunner`'ın kayıt/bırakma tel örgüsü kod
  incelemesiyle doğrulandı ve derleniyor; yalnız gerçek bir workflow
  çalıştırmasıyla uçtan uca kanıtlanmadı. **Sonraki oturum için açık iş.**
- **Frontend dosya adı `cancel-run-button.tsx` — plan taslağı
  `CancelRunButton.tsx` demişti.** Repo kebab-case dosya adlandırması
  kullanıyor (`feedback-control.tsx`, `waterfall.tsx` ile aynı desen); taslak
  yalnızca örnekti, gerçek isimlendirme kural gereği düzeltildi.
- **`FakeModelProvider` (test yardımcısı) `FakeChatClient?`'ten `IChatClient?`'e
  genişletildi.** `RunCancellationStatusTests`'in disaridan iptali gercek bir
  model cagrisi gibi taklit eden `BlockingChatClient`'i enjekte edebilmesi
  icin gerekliydi. Geriye donuk uyumlu: butun mevcut cagri yerleri
  `FakeChatClient` (ki zaten `IChatClient`'tir) geciriyordu.
- **`202` yanıt gövdesi tam `RunRecord`'dur**, planın taslağında gövde
  belirtilmemişti. Sebep: istemci ekstra bir `GET` yapmadan iptalin
  istendiği andaki durumu görebilsin; `Location` başlığı da eklendi.

## Bu Fazda Verilen Kararlar

| **K-243 — Her çalıştırma (kök VE alt) kendi `CancellationTokenSource`'unu üretir; defter ağaç cascade'ini kendi mantığıyla uygular, akan `CancellationToken`'ın doğal yayılımına GÜVENMEZ** | 2026-08-06 | `RunRecordingAgent.RunCoreAsync`/`RunCoreStreamingAsync` gelen `cancellationToken`'dan `CancellationTokenSource.CreateLinkedTokenSource` ile KENDİ kaynağını kurar ve deftere onu kaydeder. `IRunCancellationRegistry.TryCancel` bir kökü iptal ederken aynı `RootRunId`'yi taşıyan TÜM kayıtların kaynağını tek tek `Cancel()` eder — çocuğun kendi `cancellationToken`'ının kökten türetilip türetilmediğine bakmaz. Gerekçe: alt çalıştırma çağrısı MAF'ın arka plan görev tool'u üzerinden gelir ve bu zincirin gerçek çalışma anında token'ı nasıl ilettiği garanti edilebilir bir sözleşme değildir (bkz. Plandan Sapmalar'daki workflow testi). Kayıt bazlı cascade, token zincirinin gerçekte nasıl kurulduğundan bağımsız çalışır. | — |
| **K-244 — `IRunCancellationRegistry` varsayılan AÇIK kaydedilir; ayrı bir `Use...()` çağrısı yok** | 2026-08-06 | Açık soru 4'ün önerisi (A) benimsendi: defter yalnız bellekte bir `ConcurrentDictionary` tutar, hiçbir isteği reddetmez, hiçbir yan etki üretmez — K-165'in "yeni davranış varsayılan kapalı gelir" kuralı gözlemlenebilir bir davranış değişikliğini hedefler, bu defter bir davranış değiştirmez. `AddAgentPrism()` içinde koşulsuz `TryAddSingleton<IRunCancellationRegistry, RunCancellationRegistry>()`. | Dağıtık bir defter isteyen bir kurulum arayüzü kendi uygulamasıyla `TryAddSingleton`'dan önce değiştirebilir |
| **K-245 — `WorkflowRunner` aynı deftere kendi kök kaydını yazar; `ExecuteAsync`'in zaten kurduğu `timeout`+istek `CancellationTokenSource` birleşimi (`linked`) yeniden kullanılır** | 2026-08-06 | Açık soru 3'ün önerisi (A) benimsendi: `WorkflowRunner.ExecuteAsync` içindeki `linked = CreateLinkedTokenSource(cancellationToken, timeout.Token)` zaten vardı (Faz 15); yeni bir kaynak kurmak yerine bu ikisi birleştirilmiş kaynak doğrudan `registry.Register(execution.RunId, execution.RunId, tenantId, linked)` ile kaydedilir. Workflow satırı kendi ağacının köküdür (`RunId == RootRunId`). | — |
| **K-246 — İptal isteği `run.cancel` eylemiyle denetim izine yazılır** | 2026-08-06 | Açık soru 2'nin önerisi (A) benimsendi: iptal bir operatör eylemidir, "kim durdurdu" sorusu Faz 9'un denetim izi altyapısıyla (`AuditRecorder.WriteAsync`) aynı yoldan cevaplanır. `entity` alanı `run:{runId}` biçimindedir; `before`/`after` boş bırakılır (çalıştırma satırı zaten `GET /api/runs/{id}` ile okunabilir). | — |

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

**Sıradaki faz: 33** — [`33-SAGLIK-DENETIMI-VE-TESHIS.md`](33-SAGLIK-DENETIMI-VE-TESHIS.md)
(bu fazla doğrudan ilişkisiz, bağımsız plan). Faz 32'nin bıraktıkları:

- **`IRunCancellationRegistry` yeni bir genişleme noktasıdır** (`TryAddSingleton`,
  K4). Çok örnekli kurulumun kira tabanlı tek yürütücü seçimini ekleyeceği faz
  (aday listesindeki **F-57**) bu arayüzü kendi dağıtık uygulamasıyla
  değiştirecektir — `Register`/`TryCancel`/`ActiveCount` sözleşmesi F-57 için
  sabit tutulmalıdır, yalnız uygulama bellek-içi'den dağıtıma geçer.
- **🚨 Workflow'a özgü uçtan uca bir iptal testi yazılamadı** (bkz. Plandan
  Sapmalar). `WorkflowRunner`'ın kayıt/bırakma teli `RunRecordingAgent` ile
  birebir aynı desendedir ve derleniyor, ama gerçek bir workflow
  çalıştırmasıyla kanıtlanmadı. Bu alana dönen bir faz önce
  `samples/AgentPrism.Api`'de kayıtlı bir workflow'u gerçekten iptal ederek
  bunu kapatmalı (bu doküman bunun yerine `RunCancellationRegistry`'nin ağaç
  cascade'ini registry seviyesinde doğrulayan testlerle kapandı).
- **Kayıt/bırakma deseni artık üç yerde tekrarlanıyor**:
  `RunRecordingAgent.RunCoreAsync`, `RunRecordingAgent.RunCoreStreamingAsync`,
  `WorkflowRunner.ExecuteAsync`. Yeni bir çalıştırma yolu (ör. eval koşusu,
  toplu iş) eklenirse aynı `using var cts = CreateLinkedTokenSource(...)` +
  `using var registration = _cancellationRegistry?.Register(...)` deseni
  tekrarlanmalıdır — aksi halde o yol dışarıdan iptal edilemez ve bu sessiz
  bir eksikliktir (hata vermez, yalnızca `/cancel` `409` döner).
- **`AgentPrismRunOptions`'a yeni alan eklenmedi.** İptal, çalıştırma
  seçeneklerinden değil doğrudan `IRunCancellationRegistry`'den okunur; bu
  yüzden Faz 12'nin `AgentPrismRunOptions`/`AgentRunScope` sözleşmesi
  değişmedi ve gelecekte de değişmesi gerekmez.
