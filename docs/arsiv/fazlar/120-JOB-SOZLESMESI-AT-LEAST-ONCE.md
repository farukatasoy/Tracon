# Faz 120 — `IJobHandler` Sözleşmesi: At-Least-Once Yazılı Hale Gelir

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) — BL-041 (yayın denetimi bulgusu, aday listesinden değil)
> **Önkoşul:** Yok — [Faz 119](119-HATA-METNI-SIZINTISI.md) ile aynı dosyaya (`JobWorkerBackgroundService.cs`) dokunur; **119 önce kapanırsa çakışma olmaz**
> **Paketler:** `AgentPrism.Abstractions`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — yalnız `Testing.Contracts.Xunit` içinde yeni contract sınıfı
> **Tüketici yüzeyi:** `docs-site/` — job/scheduling rehberi · sevk edilen: `IJobHandler` ve `JobContext.Items`'ın XML dokümanı (asıl iş budur)
> **Manuel test alanı:** `docs/manuel-test/` — mevcut zamanlama/job ailesine eklenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 94e0b19:docs/arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md
> ```
>
> Damıtıldı 2026-08-27 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`IJobHandler` üçüncü tarafın yazacağı bir genişleme noktasıdır. Bugün onun sözleşmesi, **bir handler'ın aynı iş için birden fazla kez çağrılabileceğini hiç söylemiyor.** Yerleşik üç handler bunu savunmacı bir kontrolle kendi içinde çözüyor; kural yalnız o üç dosyanın yorumunda yaşıyor.

## Bitiş Ölçütleri (DoD)

- [x] `IJobHandler.ExecuteAsync` ve `JobContext.Items` dokümanı at-least-once'ı, süzülmemiş `Items`'ı ve handler'ın sorumluluğunu açıkça söyler
- [x] `JobHandlerContract` var ve **üç yerleşik handler** tarafından türetiliyor
- [x] En az bir **dış sample** (`PackageReference`, `ProjectReference` yok) contract'ı koşuyor — `AgentPrism.Samples.CustomJobHandler(.Tests)`
- [x] `JobLeaseExpiryTests` dokümante edilen davranışın gerçekten var olduğunu **ölçüyor** (doküman runtime'dan güçlü garanti vermiyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 11df16c` yeşil (build, 3153 test, pack, format, docs-site dört alt kapısı)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. § *Gerçekleşen Public API* altındaki koşum notu
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/` içine eklendi — `MT-JOB-103`, `MT-JOB-104` (`16-IS-KUYRUGU-VE-ZAMANLAMA.md`)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 3× 🟡 kapatıldı, 2× 🟢 not edildi (bkz. § *Denetim Bulguları*)
- [x] `docs-site/` job/scheduling rehberi at-least-once'ı anlatıyor — yeni `guides/write-your-own-job-handler.md` + `background-work.md` güncellemesi
- [x] [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) güncellendi (BL-041 kapandı)

### Doğrulama komutları

```bash
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests \
  --filter-method "*JobHandlerContract*"
```

---

## Plandan Sapmalar

- **`JobHandlerContract`'ın taslak imzası gerçekleşmedi, aynı ailedeki emsallerin (`AgentSourceContract`, `RunJudgeContract`) desenine geçti.** Plan yalnız `protected abstract IJobHandler Handler { get; }` + `CreateItem(...)` öngörüyordu. Gerçekte: `IAsyncLifetime` tabanlı, `CreateHandlerAsync()` (async kurulum — Eval kendi suite/case/run kaydını burada yapıyor), `JobId` (paylaşılan job kimliği; Eval kendi run kaydını buna bağlıyor) ve `virtual CreateJob(items)` (hedef ad/payload override'ı) eklendi. Sebep: `EvalJobHandler` bir suite + case + run kaydı olmadan çalışamıyor, bu kurulum async ve tek bir `Handler` property'sine sığmıyor.
- **`AgentPrism.Testing` paket referansı planın taslak sample kurulumunda yoktu, gerçekte de eklenmedi** — `NightlyReportJobHandler`in `AgentPrismTestHost` gibi bir gerçek-run altyapısına ihtiyacı yok (bkz. aşağıdaki JobKind bulgusu: gerçek bir host üzerinden dispatch edilemiyor, bu yüzden öyle bir test yanlış yolu doğrulardı).
- **`docs-site/guides/write-your-own-job-handler.md` planda yoktu, uygulama sırasında eklendi.** Diğer tüm extension seam'lerinin (`write-your-own-judge.md`, `-tool.md`, `-agent-source.md`, `-store.md`, `-error-classifier.md`) kendi rehber sayfası varken `IJobHandler`'ın yoktu; tutarlılık için eklendi.
- **Kod dışı, kapsam dışı bir bulgu doğrulandı ve belgelendi, düzeltilmedi:** `JobWorkerBackgroundService.ExecuteJobAsync`'in `handlers.FirstOrDefault(h => h.Kind == job.Kind)` dispatch'i, DI'nin `IEnumerable<IJobHandler>`'ı kayıt sırasına göre çözmesi yüzünden, `AddAgentPrism()` içinde ÖNCE kayıtlı yerleşik handler'ı her zaman kazandırıyor. Bugün **9/9 `JobKind` değerinin** zaten bir yerleşik handler'ı var, yani `AddJobHandler<T>()` ile (dokümante edilen tek örnekteki gibi `AddAgentPrism()`'den SONRA) eklenen bir özel handler mevcut hiçbir `Kind` için gerçek dünyada asla dispatch edilmiyor. Bu, fazın kapsamının çok üstünde bir mimari karar gerektiriyor (dispatch sırasını "son kazanır"a çevirmek mi, `JobKind`'i string'e açmak mı, yoksa mevcut hâliyle mi bırakmak). `docs/hafiza/aspnetcore-di.md`'ye tuzak notu yazıldı, `docs-site` rehberine `:::caution` kutusu eklendi, `AddJobHandler<T>()`'ın XML dokümanına tek cümlelik uyarı eklendi — kod DEĞİŞMEDİ. Önerilen sonraki adım: `aday-kesfi` ile bir F-NN adayı üretilmesi.
- **Kapsam dışı, tesadüfen karşılaşılan bir tooling kusuru düzeltildi:** `scripts/kapi.py`'nin kapanış zincirindeki `dotnet format AgentPrism.slnx --verify-no-changes --no-restore` komutu, tam solution test koşumunun (400s+) hemen ardından çalıştığında `samples/*.Tests` projelerinin (floating-version, local-feed paket tüketicisi altı örnek çifti) tiplerini bulamıyordu — üç bağımsız `kapi.py kapanis` koşumunda tekrarlanabilir şekilde ölçüldü, izole çalıştırıldığında hiç görülmedi. `--no-restore` kaldırıldı (restore hiçbir şey değişmemişse birkaç saniye); `scripts/kapi_test.py`'deki ilgili assertion güncellendi. Kök neden tam açıklanamadı (MSBuildWorkspace'in restore'suz workspace yüklemesiyle ilgili, muhtemelen tam test koşumunun bıraktığı bir durumla etkileşiyor); iz `docs/hafiza/build-ve-analyzer.md`'ye (mevcut MSBuildWorkspace/restore tuzakları ailesine) eklenebilir — bu fazda eklenmedi, düşük öncelik.

## Bu Fazda Verilen Kararlar

- **K-641** — `IJobHandler`'ın at-least-once yürütme sözleşmesi public XML dokümana yazılır ve `JobHandlerContract` ile kilitlenir; `IIdempotencyStore`'u job dispatch loop'una bağlamak reddedilir (bkz. `docs/KARARLAR.md`).

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, `11df16c` tabanına karşı) **🔴 bulgu üretmedi**. Üç 🟡 ve iki 🟢 bulgu:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `AddJobHandler<T>()`'ın kendi XML dokümanı (paketle giden, IntelliSense'te görünen birincil kanal) dispatch-order sınırlamasından hiç bahsetmiyordu — yalnız `docs-site`'ta vardı | **Düzeltildi** — `<remarks>`'e tek cümlelik uyarı eklendi |
| 2 | 🟡 | `background-work.md`'nin "Read next" listesinden gerekçesiz olarak `Production deployment` bağlantısı kaldırılmıştı (4→3 link sınırı için), oysa sayfa konuyla ilgiliydi | **Düzeltildi** — `Inbound triggers` yerine değiştirildi, `Production deployment` geri eklendi |
| 3 | 🟡 | Kapsam dışı mimari bulgu (`FirstOrDefault` dispatch + kapalı `JobKind`) yalnız `docs-site` caution kutusunda yaşıyordu, `docs/hafiza/`'da veya aday listesinde kayıtlı değildi | **Kısmen düzeltildi** — `docs/hafiza/aspnetcore-di.md`'ye tuzak notu eklendi. `docs/ADAYLAR.md`'ye F-NN olarak eklenmedi: o dosyanın kendi süreci (`aday-kesfi` skill'i, sekiz mercekli yargı) atlanmadan mekanik ekleme yapmak dosyanın kendi yönetişimini ihlal eder — bu fazın "Sonraki Faza Devir Notu"na taşındı |
| 4 | 🟢 | Rehberin önerdiği "AddAgentPrism()'den önce kaydet" çözümü hiçbir testte kanıtlanmıyor | Devredilmedi — rehber zaten kendi iddiasını "until this ordering limitation is resolved" diyerek sınırlıyor, yanıltıcı değil |
| 5 | 🟢 | `dotnet format --no-restore` MSBuildWorkspace tuzağı yalnız `kapi.py` kod yorumunda yaşıyor, `docs/hafiza/build-ve-analyzer.md`'ye taşınabilirdi | Devredilmedi — küçük, düşük risk, kod yorumu yeterince açıklayıcı |

Denetim ayrıca bağımsız olarak doğruladı: `dotnet build` (0 uyarı), yeni testlerin tamamı (`JobHandlerContract*` 10/10, `*LeaseExpiry*` 1/1, dış sample 5/5), `kapi_test.py` (38/38), `docs-site` dört kapısının tamamı, `secret` taraması (0 sonuç). Test tiyatrosu yok (3.2 temiz), test seviyesi doğru (3.3 temiz — contract testleri handler sınırında, `JobLeaseExpiryTests` depo sınırında), imza-gövde kayması yok (3.5 temiz), repo kuralları temiz (3.7).

## Sonraki Faza Devir Notu

- **BL-041 kapandı; YAYIN-HAZIRLIK.md'deki 4 preview-blocker kusur sınıfının tamamı artık kapalı.** Kalan iş kusur değil, karar: §7 (UR-003, public API freeze taraması) ve §8 (OP-001..009, NuGet.org operasyon kararları). Sıradaki adım büyük olasılıkla **`nuget-danismani`'nin yeni bir turu** — nihai "yayınlanabilir mi" kararını bu iki karar grubu üzerinden verecek.
- **🚨 Yeni bir "F-NN aday üret" turu (`aday-kesfi`) çalıştırılırsa şu bulguyu girdiye ekle:** `IJobHandler`/`AddJobHandler<T>()` genişleme noktası, mevcut 9 `JobKind` değerinin hiçbiri için üçüncü tarafça gerçekten kullanılamıyor — `JobWorkerBackgroundService`'in `FirstOrDefault(h => h.Kind == job.Kind)` dispatch'i DI kayıt sırasına bağlı ve yerleşik handler'lar her zaman önce kayıtlı. Tam kanıt: `docs/hafiza/aspnetcore-di.md` (Faz 120 notu), `docs-site/guides/write-your-own-job-handler.md`'deki caution kutusu. Olası çözüm yönleri (hiçbiri bu fazda değerlendirilmedi): dispatch sırasını "son kazanır"a çevirmek, `JobKind`'i açık bir string'e dönüştürmek, veya mevcut sınırı kalıcı olarak kabul edip yalnız AgentPrism'in kendi iç modüllerinin (Eval gibi) kullanacağı bir seam olarak yeniden çerçevelemek.
- Bu fazdan sonra planlanmış bir F-NN yok — `docs/YOL-HARITASI.md`'de 120 son kalemdir. Sıradaki iş kullanıcı kararına bağlı: yeni bir `aday-kesfi` turu mu, yoksa doğrudan `nuget-danismani`'nin yayın kararı turu mu.
