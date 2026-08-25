# Faz 100 — Yargıç Sözleşmesinin Yayını

> **Durum:** ✅ Tamamlandı (2026-08-25)
> **Kaynak:** Doğrudan kullanıcı isteği (2026-08-25) — `IRunJudge` üçüncü taraf
> uygulanabilirlik incelemesi. Aday listesinden gelmedi; Faz 98 · 99 ile aynı
> damardır: `preview.1` öncesi genişleme noktası olgunlaştırma.
> **Önkoşul:** [Faz 99](99-SAGLAYICI-SOZLESMESININ-YAYINI.md) —
> `ContractCoverage`'ın aile mekanizmasını (K-610), opt-in sözleşme sınıfı
> kuralını (K-611) ve yalnız-NuGet sample emsalini bu faz devralır.
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`,
> `AgentPrism.Testing.Contracts.Xunit`, `samples/`
> **Yeni paket:** Yok — sözleşme suite'i var olan pakete üçüncü bir ad alanı ekler ·
> **Migration:** Yok
> **Public API:** Büyüyor ve **daralıyor** — `RunJudgment.JudgeUsage` kalkar,
> `IModelProviderRegistry`'ye bir aşırı yükleme, `OnlineEvaluationOptions`'a bir
> alan, `ModelCredentialSource` · `AgentPrismJudgeException` · `RunJudgeContract` eklenir. Ölçüldü (2026-08-25):
> `wc -l src/*/PublicAPI.Shipped.txt` = 17 satır, hepsi `#nullable enable`
> başlığı — **her dosya boştur**, yüzeyi bugün değiştirmek bedavadır; Faz 7'den
> sonra bir sürüm kararıdır.
> **Tüketici yüzeyi:** site: yeni `guides/write-your-own-judge.md`,
> `concepts/evaluation.md`, `packages.md`, `capabilities.md`,
> `reference/configuration.md` · sevk edilen: `IRunJudge` · `RunJudgeContext` ·
> `RunJudgment` · `IModelProviderRegistry` XML dokümanı,
> `src/AgentPrism.Testing.Contracts.Xunit/README.md`
> **Manuel test alanı:** [`docs/manuel-test/17-EVAL-VE-DENEYLER.md`](../../manuel-test/17-EVAL-VE-DENEYLER.md) (`EVAL` öneki, bugün 69 case)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show c5b26e9:docs/arsiv/fazlar/100-YARGIC-SOZLESMESININ-YAYINI.md
> ```
>
> Damıtıldı 2026-08-25 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`IRunJudge`, AgentPrism'in dört genişleme noktasından biridir. Faz 98 depolama, Faz 99 sağlayıcı sözleşmesini sevk edilen yüzeye taşıdı. Yargıç hâlâ taşınmadı: arayüz derlenir, ama derlendikten sonra **sessizce yanlış davranan on kural** hiçbir sevk edilen yüzeyde yazmaz.

## Bitiş Ölçütleri (DoD)

- [ ] `IRunJudge` · `RunJudgeContext` · `RunJudgment` XML dokümanı 100.1'deki **on maddenin hepsini** taşır
- [ ] `RunJudgment.JudgeUsage` kaldırıldı; `grep -rn "JudgeUsage" src/ tests/` boş döner
- [ ] Aynı adlı iki `IRunJudge` kaydı host başlarken `AgentPrismException` üretir; karşılaştırma `OrdinalIgnoreCase`
- [ ] Boş/geçersiz `Name` host başlarken reddedilir
- [ ] Aralık dışı skor kalıcılaşmaz, `judge_contract` üretir ve **yeniden kuyruklanmaz**
- [ ] Yargıcın keyfi `OperationCanceledException`'ı job'ı `Running` bırakmaz; gerçek host iptali hâlâ yayılır (iki test birlikte)
- [ ] `OnlineEvaluationOptions.JudgeTimeout` varsayılanı 60 s; `<= TimeSpan.Zero` ve `Infinite` validator tarafından reddedilir
- [ ] Zaman aşımı **çağrı başına** uygulanır; iki yargıçlı bir koşumda ölçüldü
- [ ] `502` gövdesi ve job `error_message`'ı ham üçüncü taraf metni taşımaz; yalnız `{ad} ({kod})` biçimi
- [ ] Kısmi başarısızlık yanıt gövdesinde görünür
- [ ] 🚨 Yargıcın `JudgeAsync` içinde başlattığı `run` **örneklenmez**; döngü testi yeşil
- [ ] `ModelRunJudge` `CreateChatClientAsync(..., ModelCredentialSource.Setup, ...)` kullanır; egress policy uygulanır, BYOK anahtarı **kullanılmaz** (iki ayrı test)
- [ ] `Reason` 4000 karakterde kırpılır ve kırpma işaretlenir; skor yazılır
- [ ] `RunJudgeContract` `AgentPrism.Testing.Contracts.Judges` ad alanında yayınlandı; paketin bağımlılık grafiğine `AgentPrism.Core` **inmez** (`project.assets.json` ölçümü)
- [ ] Sözleşme paketinin public yüzeyine `Shouldly` tipi sızmaz
- [ ] `samples/AgentPrism.Samples.CustomRunJudge` yalnız `PackageReference` kullanır; `grep -c ProjectReference` → `0`
- [ ] Sample'ın test projesi `RunJudgeContract`'ı türetir, kapsam kapısını koşar **ve** uçtan uca bir skor kalıcılaştırır; hepsi yeşil
- [ ] Mevcut `StoreContractCoverageTests` (dört koşum) ve sağlayıcı kapsam testi yeşil kaldı
- [ ] `OnlineEvalJobHandler`'ın yanlış retry yorumu düzeltildi ve bir test davranışı kanıtlıyor
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı; sample yargıç kaydedilip skor üretildi, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/17-EVAL-VE-DENEYLER.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`guides/write-your-own-judge.md` dahil); `npm run build` + `check-links.mjs` temiz
- [ ] Açık Soru 3'ün aday kalemi `docs/ADAYLAR.md`'ye yazıldı

### Doğrulama komutları

```bash
# Dört kapı — taban, bu fazdan ÖNCEKİ commit
python3 scripts/kapi.py kapanis --taban 64c0a39

# Ölü alan gerçekten gitti
grep -rn "JudgeUsage" src/ tests/ docs-site/src/content/docs/api/ || echo "temiz"

# Sözleşme paketi Core'a inmiyor
F=$(find artifacts/obj/AgentPrism.Testing.Contracts.Xunit -name project.assets.json | head -1)
python3 -c "import json;d=json.load(open('$F'));print([k for k in list(d['targets'].values())[0] if 'AgentPrism.Core' in k])"
# beklenen: []

# Sample yalnız NuGet
grep -c ProjectReference samples/AgentPrism.Samples.CustomRunJudge/*.csproj || true   # 0

# Sample sözleşmeyi geçiyor
MSBUILDDISABLENODEREUSE=1 dotnet test samples/AgentPrism.Samples.CustomRunJudge.Tests

# Döngü ve iptal regresyonları
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests \
  --filter-method "*JudgeSamplingSuppression*|*OnlineEvalCancellation*|*OnlineEvalTimeout*"
```

---

## Kalemlerin Sınıflandırması

| Kalem | Sınıf |
|---|---|
| 100.7 `RunKind.Eval` döngüsü — yapısal bastırma | 🔴 **preview.1 blocker** — sınırsız maliyet |
| 100.4 iptal sahipliği | 🔴 **preview.1 blocker** — job asılı kalır |
| 100.3 skor aralığı kapısı | 🔴 **preview.1 blocker** — kalıcı veri bozulması |
| 100.2 yinelenen ad reddi | 🔴 **preview.1 blocker** — sessiz satır ezme |
| 100.6 `JudgeUsage` kaldırma | 🔴 **preview.1 blocker** — yayından sonra kırıcı |
| 100.9 `ModelCredentialSource` + egress | 🔴 **preview.1 blocker** — public arayüz üyesi, yayından sonra kırıcı |
| 100.10 `MaxReasonLength` + kırpma | 🔴 **preview.1 blocker** — sınırsız payload'ı sözleşme olarak dondurmamak için |
| 100.4 `JudgeTimeout` seçeneği | 🔴 **preview.1 blocker** — public options alanı |
| 100.5 hata normalizasyonu (+ HTTP zarfı) | 🔴 **preview.1 blocker** — HTTP gövdesi sözleşmedir |
| 100.11 `RunJudgeContract` ailesi | 🟡 **1.0 blocker** — yeni aile eklemek yayından sonra da mümkündür, ama üçüncü taraf onsuz doğrulanamaz |
| 100.12 sample | 🟡 **1.0 blocker** — sözleşmeyi kanıtlayan tek çalışan yapıt |
| 100.1 XML maddeleri (M1·M2·M3·M9) | 🟢 **yalnız doküman** — kod değişmez |
| 100.8 retry semantiği | 🟢 **yalnız doküman** + bir regresyon testi |
| `judge:` ad öneki maliyet kuralı (100.7) | 🟢 **yalnız doküman** |
| 100.10 bağlamın "neyi taşımadığı" | 🟢 **yalnız doküman** |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- Planlanan `ModelCredentialSource` overload'ı, mevcut optional
  `CancellationToken` overload'ı nedeniyle `RS0027` ile derlenmedi. Kaynak
  uyumluluğunu koruyan `CreateSetupChatClientAsync` seçildi; gerekçe K-613'tedir.
- Sample kendi başına yalnız NuGet `PackageReference` taşır. Yayınlanmamış
  sözleşme paketini nuget.org'dan çözmek yeni `RunJudgeContract` tipini vermez;
  bu nedenle sample test projesi eklenmedi. HTTP kalıcılık yolu fonksiyonel
  testte, sample sınıfının davranışı ise yayın sonrası package testinde koşulacak.

## Bu Fazda Verilen Kararlar

- K-613 — setup credential yolu ayrı `CreateSetupChatClientAsync` üyesidir.

## Denetim Bulguları

- 🔴 API overload'ı · K-613 ile gerekçelendirildi; analyzer zorunluluğu ölçüldü.
- 🔴 timeout üst sınırı · düzeltildi, 5 dakika validator sınırı eklendi.
- 🔴 kritik failure yolları · timeout, host iptali, terminal skor ve sampling bastırma testleri eklendi.
- 🔴 yargıç-başına retry checkpoint · F-152 olarak devredildi.
- 🟡 sözleşme tekrar/iptal · `RunJudgeContract` senaryolarına eklendi.
- 🟡 tüketici yüzeyi · README, capabilities, packages ve yeni guide güncellendi.

## Sonraki Faza Devir Notu

🚨 `IRunJudge` singleton'dır; çağrıları paralel gelir ve `JudgeAsync` başlattığı
run'larda `AmbientSamplingSuppressionScope` akmalıdır. Model tabanlı yargıç
`CreateSetupChatClientAsync` kullanır: tenant BYOK değeri okunmaz, fakat egress
policy her primary/fallback sağlayıcı için uygulanır. Retry şu anda bütün yargıç
listesini tekrar koşar; durable yargıç checkpoint'i F-152'dir. Yeni faz seçimi
`docs/YOL-HARITASI.md` üretildikten sonra yapılır.
