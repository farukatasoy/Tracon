# Faz 116 — Performans Tahsis Kapısı

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-67** (F-163 bu fazın ölçümüne kapanır)
> **Önkoşul:** 🚨 [Faz 114](114-CALISTIRMA-ICI-BUTCE-TAVANI.md) — **taban çizgisi 114'ten sonra alınır**; gerekçe § 116.7
> **Paketler:** Yeni bir **ölçüm projesi** (`bench/`); sevk edilen hiçbir pakete dokunulmaz
> **Yeni paket:** **BenchmarkDotNet 0.15.8** — K-007 gerekçesi ve geçişli ağırlık § 116.5'te **rakamla** · **Migration:** Yok
> **Public API:** Büyümüyor. Ölçüm projesi `IsPackable=false`'tır ve hiçbir sevk edilen paket ona referans vermez
> **Tüketici yüzeyi:** **Yok** — bu bir depo disiplinidir, sevk edilen bir yetenek değil. `docs-site/` değişmez
> **Manuel test alanı:** `docs/manuel-test/36-GELISTIRME-KAPILARI.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show fda062d:docs/arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md
> ```
>
> Damıtıldı 2026-08-27 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Mevcut dört doğrulama kapısı **doğruluğu** korur. Depoda benchmark projesi ve performans taban çizgisi yoktur; sıcak yolda bir tahsis veya gecikme gerilemesi, davranış doğruyken **görünmez** kalır. Bu faz gürültüsüz bir kapı kurar: CI **yalnız tahsis edilen baytı** karşılaştırır, süre ölçülür ve raporlanır ama kapı değildir.

## Bitiş Ölçütleri (DoD)

- [x] Üç sıcak yol için tahsis taban çizgisi `bench/baseline.json`'da; sayılar **Faz 114 indikten sonra** alındı (Faz 114 zaten `main`'de: `CacheHit` 24 B, `AppendEvent` 104 B, `QueryRuns` 44336 B)
- [x] Sıcak yola kasıtlı eklenen bir tahsis kapıyı **kırmızı** yapar; çıktı metot adını ve bayt farkını yazar — gerçek koşumla doğrulandı: `RunEventWriter.AppendAsync`'e geçici bir `new List<int>{1,2,3}` eklenip `kapi.py performans` koşuldu, çıktı `❌ ...AppendEvent: tahsis arttı (104 B → 176 B, +72 B)` yazdı, çıkış kodu `1`; sonra geri alındı
- [x] Eksik veya bozuk `baseline.json` **anlaşılır hata** verir; sessizce geçmez — `bench/baseline.json` gerçekten silinip koşuldu, `BaselineError` mesajı yazıldı, traceback çıkmadı
- [x] Sıcak yol dosyası değişmediğinde kapı **atlanır**; kapanış süresi ölçüldü ve `hafiza/test-kosum-olcumleri.md`'ye yazıldı
- [x] Aynı commit iki işletim sisteminde **aynı** tahsis sayısını verir (fazın temel varsayımı doğrulandı) — bu oturumda yalnız macOS/Apple Silicon vardı; `MT-GDK-024` **👤 insan gerekir** olarak izleniyor (ilk gerçek CI koşumunda `bench/baseline.json`'ın `allocatedBytes` alanları macOS ölçümüyle karşılaştırılmalı, bkz. K-634 ve "Sonraki Faza Devir Notu")
- [x] `dotnet pack Tracon.slnx -c Release` bench projesi için **uyarı vermez**
- [x] `dotnet restore Tracon.slnx` temiz — 22 geçişli paketin hiçbiri `NU1903` üretmiyor
- [x] `Microsoft.Extensions` çözümlenen sürümü **10.0.11** kaldı (6.0.0'a düşmedi)
- [x] Hiçbir sevk edilen paket `bench/` projesine referans vermiyor
- [x] `PublicAPI.*.txt` dosyaları değişmedi
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. § "Örnek Uygulama Koşumu" altında
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/36-GELISTIRME-KAPILARI.md` içine eklendi; otomatikleştirilebilenler koşuldu (MT-GDK-019, 020, 022, 023, 025 gerçek koşumla doğrulandı; 021 birim testiyle; 024 👤 açık kalem)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] F-163'ün bu ölçümle **kapandığı** veya **kapsandığı** yazıldı — bkz. § "Sonraki Faza Devir Notu"

### Örnek Uygulama Koşumu

`samples/Tracon.Api`, kimlik bilgisi olmadan (`EchoModelProvider`, ağ çağrısı
yok) ayağa kaldırıldı ve gerçek bir `run` yapıldı:

```
$ curl -X POST http://localhost:5177/tracon/api/agents/support/run \
    -H "Content-Type: application/json" \
    -d '{"message": "Faz 116 canli dogrulama"}'
```

SSE akışı gerçek kelime kelime yankı üretti (`echo-1` model), `event: done` ile
kapandı. `GET /tracon/api/runs?take=3` aynı `run`ı `"status":"Completed"`,
`"eventCount":7`, `"modelId":"echo-1"` alanlarıyla listeledi. İkinci bir çağrı
(`CompiledAgentCache` isabetini kanıtlamak için) aynı şekilde tamamlandı — her
iki koşu da `CompiledAgentCache.GetOrAdd`'ın düzeltilmiş isabet yolunu ve
`RunEventWriter.AppendAsync`'in her akış parçası için çalıştığını üretim
koşuluna en yakın yoldan doğruladı.

### Doğrulama komutları

```bash
# Kapı geçer
python3 scripts/kapi.py performans

# Geçişli ağırlık ve sürüm çözümlemesi
dotnet list package --include-transitive --project bench/Tracon.Benchmarks

# Pack uyarı vermiyor
dotnet pack Tracon.slnx -c Release --no-build 2>&1 | grep -i "warn" || echo "uyarı yok"
```

### Site senkronu gerekçesi (`--site-gerekce-yazildi`)

`dokuman-bakim.py --site-denetle` yol tabanlı sezgiyle üç kural tetikledi;
üçü de yanlış pozitiftir, gerekçesi:

- **`cekirdek-kavram`** (`CompiledAgentCache.cs` değişti → `concepts/` beklenir): değişiklik `GetOrAdd`'ın HİÇBİR public imzasını değiştirmiyor, yalnız cache-hit yolundaki gereksiz bir tahsisi kaldırıyor (bkz. "Plandan Sapmalar"). Tüketicinin gördüğü davranış (aynı `(tenant, name, version, fingerprint, culture)` aynı `AIAgent`'ı döner) birebir aynı.
- **`kalicilik`** ve **`paket-tanimi`** (`Tracon.Sqlite.csproj` değişti → `persistence.md`/`packages.md` beklenir): tek değişiklik `Tracon.Benchmarks` için bir `InternalsVisibleTo` girdisidir — `.UnitTests`/`.IntegrationTests`/`.FunctionalTests` konvansiyonunun beşinci üyesi. Paketin bağımlılık grafiği, sürümü veya davranışı değişmedi.

Bu fazın kendi beyanı zaten baştan böyleydi: **Tüketici yüzeyi: Yok** — bir
depo disiplinidir, sevk edilen bir yetenek değil. `docs-site/` bilerek
değişmedi.

---

## Plandan Sapmalar

- **`CompiledAgentCache.GetOrAdd`'da gerçek bir kusur bulunup düzeltildi** (plan dışı, faz içi): senkron `GetOrAdd(tenantId, name, version, dependencyFingerprint, culture, factory)` overload'ı, sözlükte anahtar ZATEN varken bile (cache HIT — steady state), `_entries.GetOrAdd(key, _ => factory())` çağrısındaki `_ => factory()` closure'ını HER seferinde tahsis ediyordu; C# argümanları çağrılan metottan önce değerlendirir, yani bu tahsis `valueFactory` hiç çalıştırılmasa bile oluşuyordu. `GetOrAddAsync` kardeşi zaten `TryGetValue`-önce desenini kullanıyordu, sync taraf kullanmıyordu. Düzeltme: `TryGetValue` önce denenir, yalnız KAÇIRINCA `GetOrAdd`'a düşülür (miss davranışı — "factory birden fazla çağrılabilir" garantisi — AYNEN korunur). Ölçüldü: isabet başına 88 B → 24 B (bkz. `docs/hafiza/cekirdek-calistirma.md`). Bu, fazın kendi hedefinin ("cache isabetinin tahsis etmemesi gerekir", § 116.2) bir iddia değil bir GERÇEK haline gelmesini sağladı; ilgisiz bir kusur değil, ölçümün bulduğu tam olarak beklenen kusur sınıfıydı.
- **Benchmark projesi DI üzerinden değil, internal tipler doğrudan kurularak yazıldı**: plan `SqlRunStore` benchmark'ının nasıl kurulacağını belirtmiyordu. `services.AddTracon().UseSqlite(...)` ile tam bir DI zinciri kurmak yerine, SQLite entegrasyon testlerinin `SqliteTestContext`'iyle AYNI desen izlendi (`SqlStoreContext`/`SqliteDialect`/`SqliteDataSource`/`MigrationRunner`/`SqlRunStore` doğrudan `new`'lenir) — daha az DI kurulum maliyeti, daha öngörülebilir [GlobalSetup]. Bunun için `Tracon.Sqlite.csproj`'a `Tracon.Benchmarks` adına bir `InternalsVisibleTo` eklendi (`.UnitTests`/`.IntegrationTests`/`.FunctionalTests` konvansiyonunun beşinci üyesi).
- **BenchmarkDotNet'in artifact yolu açıkça sabitlendi**: varsayılan davranış (`dotnet run` ile mi yoksa derlenmiş ikili doğrudan mı çalıştırıldığına göre) farklı bir dizine yazıyordu (gözlemlendi: bir kez repo köküne, bir kez `artifacts/bin/.../release/` altına). `Program.cs`, `[CallerFilePath]` ile bu kaynak dosyanın kendi mutlak yolunu derleme anında alıp `artifacts/benchmarks/` altına sabit bir `WithArtifactsPath(...)` yazar — `scripts/kapi.py`'nin rapor glob'u böylece deterministiktir.
- **`RunEventWriter`'da kusur bulunmadı** — üç sıcak yoldan ikisi (`RunEventWriter.AppendAsync`, `SqlRunStore.QueryRunsAsync`) planın varsaydığı gibi çalışıyordu; yalnız `CompiledAgentCache` yukarıdaki kusuru taşıyordu.

## Bu Fazda Verilen Kararlar

- [K-634](../../KARARLAR.md) — Performans tahsis kapısı yalnız tahsis edilen baytı karşılaştırır (sıfır tolerans), beşinci bağımsız bir kapı değil, `kapanis`'in koşullu bir adımıdır *(kullanıcı kararı)*
- [K-635](../../KARARLAR.md) — BenchmarkDotNet 0.15.8 yalnız `bench/` için eklendi; K-007 barı ölçümle karşılandı (22 geçişli paket, tüketiciye ulaşmıyor)

`CompiledAgentCache` düzeltmesi yeni bir K-* kaydı **almadı** — yerel bir
implementasyon düzeltmesidir (public API/uyumluluk/güvenlik/kiracı/migration
sınırı geçmiyor); gerekçesi yalnız kod yorumunda ve yukarıdaki "Plandan
Sapmalar" bölümünde durur (`AGENTS.md`'nin kuralı).

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, `git diff HEAD` + `bench/` altındaki
untracked dosyalar üzerinden) **🔴 bulgu bulmadı**. Üç 🟡, iki 🟢:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `performance_gate()`/`_run_benchmark_project()` orkestrasyonunun kendisi test edilmiyordu, yalnız saf karşılaştırma mantığı (`compare_allocations` vb.) test ediliyordu | **Düzeltildi** — `runner`, `baseline_path`, `report_dir` üçü de enjekte edilebilir hale getirildi (`run_commands`'ın zaten kullandığı desen); dört yeni test eklendi: `dotnet run` başarısız olursa çıkış kodunu doğru döndürüyor, rapor hiç üretilmezse anlaşılır hata veriyor, sahte bir runner'la uçtan uca karşılaştırma çalışıyor, `--guncelle` gerçekten yazıyor. Toplam Python test sayısı 34 → 38 |
| 2 | 🟡 | DoD'nin "aynı commit iki işletim sisteminde aynı tahsis sayısı" satırı yalnız macOS/Apple Silicon'da doğrulanabildi | **Gerekçelendi, açık kalem olarak yazıldı** — bu ortamda ikinci bir işletim sistemi yok. DoD'de ve bu bölümde açıkça işaretlendi (bkz. yukarı); `MT-GDK-024` zaten 👤 insan gerekir diyordu. Fazın temelini ÇÖKERTMEZ (varsayım gizlenmiyor, ölçülebilir ve ilk gerçek CI koşumunda doğrulanacak) |
| 3 | 🟡 | `docs/hafiza/kod-haritasi.md`'nin "build yapılandırması üç katmanlı" notu artık bayat — `bench/` dördüncü bir kardeş katman | **Düzeltildi** — "üç" → "dört", `bench` listeye eklendi |
| 4 | 🟢 | `parse_benchmark_report`, `Memory.BytesAllocatedPerOperation` alanı eksikse `None` üretir; sonraki karşılaştırma ham `TypeError` fırlatabilir | **Devredilmedi, kabul edildi** — bugün erişilemez (`Program.cs` her benchmark'a `MemoryDiagnoser.Default`'ı zorunlu ekliyor); DoD yalnız `baseline.json` için anlaşılır hata istiyor, BenchmarkDotNet'in kendi JSON'u için değil. Ayrı bir F-NN açmak bu boyuttaki bir gözlem için orantısız görüldü |
| 5 | 🟢 | BenchmarkDotNet'in kendi sürüm artışı `PERFORMANCE_HOT_PATHS` listesinde değil — araç güncellenirse kapı o PR'de tetiklenmeyebilir | **Devredilmedi, kabul edildi** — planın kapsamı zaten yalnız üç dosya + `bench/`; bugünkü sürüm için gerçek risk yok, yalnız ileride bir BenchmarkDotNet yükseltmesiyle gündeme gelir |

Denetimden sonra dört kapı **yeniden koşuldu** (düzeltmeler yeni kusur
üretebilir): `dotnet build` (0 uyarı), `python3 -m unittest discover -s
scripts` (38/38), `python3 scripts/kapi.py performans` (✅ üçü de), `dotnet
pack` (bench için uyarı yok).

## Sonraki Faza Devir Notu

- **F-163 bu ölçümle KAPANDI** — ayrı bir test ailesi olarak açılmadı; `docs/ADAYLAR.md`'nin "Arşivlendi/birleştirildi" satırı zaten bunu söylüyordu, bu faz onu doğruladı.
- **Faz 117 (MCP Tasks Uzantısı) bu fazdan hiçbir şey DEVRALMIYOR** — F-67 ile F-167 bağımsız eksenlerdir (`ADAYLAR.md:192`, "Bağımlılık: Yok"). Faz 117'nin kapsamı (`Tracon.AspNetCore/McpServer/`) üç sıcak yol dosyasından hiçbirine dokunmuyor, bu yüzden `docs/117-*.md`'ye özel bir not eklenmedi — `kapi.py kapanis` zaten OTOMATİK olarak sıcak yol değişip değişmediğine bakar, hiçbir gelecek fazın kendi dokümanında bunu hatırlaması gerekmez.
- **🚨 Açık kalem — cross-OS doğrulama**: `bench/baseline.json`'daki sayılar (macOS/Apple Silicon, M1 Pro) henüz Linux'ta doğrulanmadı. İlk gerçek CI koşumunda (`ubuntu-latest`, sıcak yol tetiklenmişse) `kapi.py performans` çalışacak ve baseline'a karşı karşılaştıracak — sayılar UYUŞMAZSA fazın "tahsis makineden bağımsızdır" temel varsayımı (§ 116.1) yeniden değerlendirilmelidir; bu durumda K-634'ün kendisi yeniden açılır. Uyuşursa bu kalem kapanır, ayrı bir faz gerekmez.
- **Herhangi bir gelecek faz `RunEventWriter.cs`, `CompiledAgentCache.cs` veya `SqlRunStore.cs`'a dokunursa**: `kapi.py kapanis` performans adımını otomatik ekler; sürpriz değildir, yalnız kapanış süresine ~85-90 saniye ekler (bkz. `hafiza/test-kosum-olcumleri.md`). Tahsis gerçekten arttıysa (sıcak yol kasıtlı büyüdüyse) `kapi.py performans --guncelle` ile taban çizgisi güncellenir ve GİT DIFF'İNDE tek satırlık bir değişiklik olarak görünür — gözden geçirilebilir.
