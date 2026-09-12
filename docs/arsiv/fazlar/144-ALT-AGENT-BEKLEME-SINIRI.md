# Faz 144 — Alt-Agent Bekleme Sınırı

> **Durum:** ✅ Tamamlandı (2026-09-05)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-191**
> **Önkoşul:** Yok. Kalemin tek bağımlılığı MAF 1.20.0 yükseltmesiydi; 2026-09-05'te yapıldı ve HEAD'dedir (`Directory.Packages.props:19`).
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`
> **Yeni paket:** Yok · **Migration:** Yok — yeni bir `RunEventType` üyesi şema değiştirmez (`run_events.type` zaten `smallint`)
> **Public API:** Büyüyor — Faz 7'den önce ucuz. Ölçüldü (2026-09-05): `wc -l src/*/PublicAPI.Shipped.txt` → tüm paketlerde **17 satır** (dosyalar boş). Aynı yüzeyi Faz 7'den sonra eklemek kırıcı olurdu
> **Tüketici yüzeyi:** `docs-site/src/content/docs/concepts/agents.md` (alt-agent bölümü) · `capabilities.md` (bir satır) · sevk edilen: `SubAgentSettings` XML `<example>`'ı
> **Manuel test alanı:** [`docs/manuel-test/21-DAYANIKLILIK-VE-IPTAL.md`](../../manuel-test/21-DAYANIKLILIK-VE-IPTAL.md) — case'ler oraya eklenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show ecc4da23:docs/arsiv/fazlar/144-ALT-AGENT-BEKLEME-SINIRI.md
> ```
>
> Damıtıldı 2026-09-05 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon bir agent'ın başka bir agent'ı çağırmasına izin verir. Bugün o çağrının ne kadar bekleyeceğine dair **Tracon'in seçtiği** hiçbir sınır yoktur. Bu faz sınırı iki katmanda kurar, sayıyı Tracon'e seçtirir ve zaman aşımını `run` kanıtına yazar.

## Bitiş Ölçütleri (DoD)

- [x] Token'ı okuyan asılı bir alt-agent, `ChildDeadline` süresinde iptal olur; `run` biter — `Cooperative_layer_cancels_a_child_that_reads_the_token` (`SubAgentTimeoutTests`) + gerçek koşum (aşağıya bkz.)
- [x] Token'ı yok sayan asılı bir alt-agent, `WaitTimeout` süresinde beklemeden düşer; `run` biter — `Hard_cutoff_abandons_a_child_that_ignores_cancellation`
- [x] İki davranış **harness yolunda da** kanıtlanır (ayrı test case'i) — `Harness_path_produces_the_same_hard_cutoff_behavior`, `Harness_path_produces_the_same_cooperative_layer_behavior`
- [x] Zaman aşımından sonra tamamlanan çocuk hiçbir olay, metrik veya unobserved exception üretmez — aynı test dosyası, `Release` çağrısından sonra olay sayısı sabit kaldığı ölçüldü
- [x] `(int)RunEventType.Custom == 29` testi yeşil — `RunEventTypeTests.Custom_stays_29`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 3a729fd0` (bkz. Denetim Bulguları'ndaki iki tur)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Gerçek koşum" altında
- [x] `secret` taraması boş döndü — `kapi.py tarama` içinde (kapanışın ilk adımı)
- [x] Manuel kabul case'leri `docs/manuel-test/21-DAYANIKLILIK-VE-IPTAL.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-RES-080`..`084`
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — **bağımsız değil, uygulayan oturumun kendi kendine denetimi** (bkz. Denetim Bulguları'ndaki uyarı); yedi bulgunun hepsi kapatıldı
- [x] `docs-site/concepts/agents.md` ve `capabilities.md` güncellendi; `npm run build` + `check-links.mjs` temiz — `npm run check` (dördü de: content/build/links/weight) temiz

### Gerçek koşum (`samples/Tracon.Api`, gerçek OpenRouter anahtarı, 2026-09-05)

Varsayılan `ChildDeadline` (2 dk) ile router→support zinciri normal tamamlandı
(`ChildRunStarted`/`ChildRunCompleted`, `~2.3` sn). `Tracon__AgentGraph__ChildDeadline=00:00:00.500`
ile YENİDEN başlatılıp AYNI istek gönderildiğinde gerçek bir OpenAI HTTP
çağrısı 500 ms'de kesildi:

```
"type":"ChildRunTimedOut","text":"support",
"payload":"{\"childRunId\":\"01a06f38-a975-72af-968e-307b75ef0c2d\",\"hardCutoff\":false}"
```

Router'ın son mesajı zaman aşımı metnini modelin kendi cümlesine çevirdi:
*"I can try again."* — `run` başarıyla `RunCompleted` ile kapandı, hiç
başarısız olmadı (Açık Soru 3 seçeneği B).

### Doğrulama komutları

```bash
# Zaman asimi olayi kanita girdi mi
curl -s http://localhost:5080/tracon/api/runs/<id>/events -H "Authorization: Bearer <token>" | grep -i timedout

# Custom'in sayisal degeri kaymadi
./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests --filter-class "*RunEventTypeTests*"

# Iki katman + harness parity, gercek background_agents_* akisiyla
./artifacts/bin/Tracon.AspNetCore.FunctionalTests/release/Tracon.AspNetCore.FunctionalTests --filter-class "*SubAgentTimeoutTests*"
```

---

## Plandan Sapmalar

- **🚨 Katman 2'nin uygulaması plandan tamamen farklı çıktı.** Plan katman 2'yi
  (sert kesme) MAF'ın `BackgroundAgentsProviderOptions.WaitTimeout`'una
  bırakıyordu — Tracon yalnız sayıyı kurup MAF'ın beklemesini varsayacaktı.
  Gerçek bir fonksiyonel test (`SubAgentTimeoutTests`, gerçek
  `background_agents_*` tool akışı) bunun YANLIŞ olduğunu ölçtü: tek bir
  `wait_for_first_completion` çağrısı, görev hâlâ çalışırken, configured
  `WaitTimeout` kadar beklemeden ANINDA "hâlâ çalışıyor" döndü. Çözüm:
  `ChildAgentInvoker` katman 2'yi KENDİSİ, K-621'in aynı üçlü `Task.WhenAny`
  yarışıyla (`invocation`/`waitTimeoutTask`/`callerCancellation`) uygular;
  `BackgroundAgentsProviderOptions.WaitTimeout` yalnız tutarlılık için kurulur,
  davranış garantisi ondan beklenmez. Karar kaydı: K-677.
- Test dosyaları planın önerdiği `tests/Tracon.Core.FunctionalTests/`
  projesinde DEĞİL — böyle bir proje yok. Gerçek MAF tool akışını gerektiren
  testler `tests/Tracon.AspNetCore.FunctionalTests/SubAgentTimeoutTests.cs`
  içine, saf çözümleme/doğrulama testleri `tests/Tracon.Core.UnitTests/`
  altına (`Compilation/SubAgentSettingsResolutionTests.cs`,
  `Graph/AgentGraphWaitLimitValidationTests.cs`, `Graph/ChildAgentInvokerTests.cs`
  eklemeleri, `Runs/RunEventTypeTests.cs`) yazıldı.
- `AgentDefinitionCompiler.ResolveSubAgentTimeouts` planda yoktu; çözümleme
  sırasını doğrudan test edebilmek için eklendi ve `BuildCompactionStrategy`
  ile aynı gerekçeyle `internal` yapıldı (private değil).
- `ChildAgentsBuild` (internal record) planda yoktu — `CreateChildAgents`'ın
  hem `ChildAgentInvoker` listesini hem çözümlenen `WaitTimeout`'u TEK
  çağrıda üretip iki çağırana (düz agent + harness) aktarması için eklendi.
- **Kendi bulduğumuz iki gerçek kusur, uygulama sırasında düzeltildi** (bkz.
  Denetim Bulguları): `TraconAgentGraphOptions.WaitTimeout`'un türetilen
  değeri `ChildDeadline = TimeSpan.MaxValue`'da taşabiliyordu (okumada bile,
  doğrulamadan önce); ve gerçek bir çağıran iptali, katman 2'nin "terk et"
  yarışını KAZANIRSA `ChildRunCompleted` olayı YAZILMIYORDU (davranış
  regresyonu — Faz 144 öncesi her zaman yazılırdı).
- `RunEventType.ChildRunTimedOut` eklenirken `SubAgentSettings.cs`'in
  `<example>`'ı ilk yazımda derlenmiyordu (`AgentDefinition.Model` `required`
  alanı eksikti) — `ExampleCompilationTests` bunu yakaladı, düzeltildi.
- Aynı `<example>`'ın (ve `WaitTimeout`'un `<summary>`'sindeki bir
  `<see cref="ChildDeadline"/>`'ın) OpenAPI şemasına TAM CLR imzası olarak
  sızması `docs-site`'ın `check:content` kapısıyla yakalandı; `<see cref>`
  `<c>ChildDeadline</c>`'a çevrildi ve OpenAPI/istemci zinciri yeniden
  üretildi.

## Bu Fazda Verilen Kararlar

- **K-677** — Alt-agent çağrısının iki katmanlı bekleme sınırında katman 2'yi
  (sert kesme) `ChildAgentInvoker`'ın KENDİSİ uygular; MAF'ın
  `BackgroundAgentsProviderOptions.WaitTimeout`'una güvenilmez. Tam gerekçe:
  [`KARARLAR.md`](../../KARARLAR.md) ve [`arsiv/KARARLAR-GECMISI.md`](../KARARLAR-GECMISI.md) — K-677.

## Denetim Bulguları

> 🚨 **Bağımsız `faz-denetim` denetçisi bu fazda İKİ KEZ çalıştırılmaya
> çalışıldı ve ikisi de ortam engeliyle tamamlanamadı**: ilk deneme yanlışlıkla
> izole bir git worktree'de (fazın commit edilmemiş değişikliklerini hiç
> görmeyen bir kopyada) çalıştı; ikinci deneme doğru ortamda (ana checkout)
> başlatıldı ama oturum hız sınırına (`rate_limit`, 429) takılıp yarıda
> kesildi. Üçüncü bir deneme yapılmadı. Bunun yerine **uygulayan oturumun
> kendisi**, tam diff'e karşı `faz-denetim`'in sekiz başlıklı kontrol listesini
> uyguladı — bağımsızlık garantisi bu fazda EKSİKTİR, sonraki bir oturum
> isterse gerçek bir bağımsız denetim hâlâ borçludur.

Kendi kendine denetimde bulunanlar (hepsi bu fazda kapatıldı):

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `TraconAgentGraphOptions.WaitTimeout`'un türetilen değeri (`ChildDeadline + 30sn`) `ChildDeadline = TimeSpan.MaxValue`'da `OverflowException` fırlatabiliyordu — doğrulama çalışmadan, salt OKUMADA | 🔴 | Düzeltildi: taşma `TimeSpan.MaxValue`'ya kelepçelenir (`AgentRunBudget`'ın aynı sözleşmesi); `A_ChildDeadline_near_TimeSpanMaxValue_does_not_overflow_the_derived_WaitTimeout` testiyle kilitlendi |
| 2 | Gerçek çağıran iptali, katman 2'nin "terk et" yarışını (`callerCancellation` vs `invocation`) KAZANIRSA `ChildRunCompleted` olayı YAZILMIYORDU — Faz 144 öncesi her zaman yazılırdı (davranış regresyonu) | 🔴 | Düzeltildi: hem `RunCoreAsync` hem `AdvanceAsync`, `winner != invocation/moveNext`'i yalnız `!cancellationToken.IsCancellationRequested` iken "sert kesme" sayar; gerçek iptal normal `try/finally` yoluna düşer. `Callers_own_cancellation_still_propagates_when_no_timeout_fires` testiyle kilitlendi |
| 3 | DoD'nin hata modu tablosundaki "birden çok çocuk aynı anda; ilki zaman aşımına uğrarken diğeri tamamlanır" satırı hiçbir testte yoktu | 🟡 | Düzeltildi: `One_child_timing_out_does_not_affect_a_sibling_call_that_completes_normally` eklendi |
| 4 | DoD "İki davranış harness yolunda da kanıtlanır" diyordu ama harness yolu yalnız sert kesme için test edilmişti, kooperatif katman için değil | 🟡 | Düzeltildi: `Harness_path_produces_the_same_cooperative_layer_behavior` eklendi |
| 5 | `SubAgentSettings.cs`'in ilk `<example>`'ı derlenmiyordu (`Model` required alanı eksik) | 🔴 | Düzeltildi — `ExampleCompilationTests` (regresyon kapısı zaten vardı) yakaladı |
| 6 | `WaitTimeout`'un `<summary>`'sindeki bir `<see cref="ChildDeadline"/>`, OpenAPI şemasına TAM CLR imzası olarak sızıyordu | 🔴 | Düzeltildi — `docs-site`'ın `check:content` kapısı yakaladı; `<c>ChildDeadline</c>`'a çevrildi, OpenAPI/istemci zinciri yeniden üretildi |
| 7 | `samples/Tracon.Api`'de gerçek run KANITLANMAMIŞTI (yalnız otomatik testler) | 🔴 | Düzeltildi: gerçek OpenRouter anahtarıyla iki gerçek koşum yapıldı — biri varsayılan (2 dk) sınırla normal tamamlanan router→support zinciri, biri 500 ms `ChildDeadline` ile GERÇEK bir OpenAI HTTP çağrısını ortasından kesen kooperatif zaman aşımı (`ChildRunTimedOut`, `hardCutoff:false`, model "I can try again." diyerek zarifçe devam etti) |

**🔴 ve 🟡 kalmadı** (yukarıdaki yedisi de kapatıldı) — ama madde 0'daki
bağımsızlık eksikliği açık bir devir notu olarak kalıyor.

## Sonraki Faza Devir Notu

- **Devralınan sözleşme:** Bir alt-agent çağrısının iki katmanlı bekleme
  sınırı artık `ChildAgentInvoker`'ın KENDİ sorumluluğudur —
  `BackgroundAgentsProviderOptions.WaitTimeout` yalnız MAF'ın kendi iç
  mekanizmasıyla tutarlılık için kurulur, davranış garantisi ondan
  beklenmemelidir (K-677). MAF bir sonraki sürümde `WaitTimeout`'un anlamını
  değiştirse bile Tracon'in garantisi etkilenmez.
- **🚨 Bilinen tuzak:** `background_agents_wait_for_first_completion` tek
  çağrıda configured `WaitTimeout` kadar BEKLEMEZ — kısa bir yoklama gibi
  davranır (ölçüldü, `docs/hafiza/maf-api.md`). MAF'ın background-agent
  mekanizmasına dokunan bir sonraki faz bunu MUTLAKA gerçek bir tool akışıyla
  ölçmeli, reflection'a güvenmemelidir.
- **🚨 İkinci tuzak (bu fazda iki kez yakalanan sınıf):** `<see cref>` — hatta
  `<example>` içindeki eksik bir `required` alan — sevk edilen bir DTO'nun
  `<summary>`'sinde kullanılırsa OpenAPI şemasına TAM CLR imzası olarak
  sızabilir. Yalnız `<summary>` OpenAPI `description`'a girer (`<remarks>`
  girmez); tüketiciye görünecek bir tip/üye adını ANMAK gerekiyorsa
  `<c>Ad</c>` kullan, `<see cref>` değil.
- **Yer tutucu / açık uç:** Yok. Planın üç açık sorusunun üçü de (B/B/B)
  uygulandı ve karar defterine (K-677) yazıldı.
- **Bağımsız denetim borcu:** Yukarıdaki "Denetim Bulguları" başlığındaki
  uyarıyı gör — bu faz kendi kendine denetlendi, taze bağlamlı bir denetçi
  tarafından DEĞİL. Sıradaki faz açılışında (veya bir sonraki `faz-denetim`
  fırsatında) bu fazın diff'i istenirse hâlâ bağımsız gözden geçirilebilir.
- **Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek.
