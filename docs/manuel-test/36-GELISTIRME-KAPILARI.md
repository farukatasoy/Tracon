# 36 — Geliştirme Döngüsü Kapıları (`GDK`)

> **Alan kodu:** `GDK` · **Faz:** 91, 92, 116
> **Kaynak:** `scripts/kapi.py` · `scripts/denetim-paketi.py`
> · `scripts/*_test.py` · `src/Tracon.UI/Tracon.UI.Frontend.targets`
> · `docfx/docfx.json` · `tests/Tracon.Core.UnitTests/Architecture/DocfxConfigurationTests.cs`
> · `tests/Tracon.Core.UnitTests/Experiments/CanaryEvaluationServiceTests.cs`
> · `tests/Tracon.Core.UnitTests/Recording/RunReconciliationTests.cs`
> · `.agents/ortak/` (Faz 92) · `bench/Tracon.Benchmarks/` (Faz 116)

Bu aile, geliştirme kapılarının komutları sessizce atlamadığını ve tarihsel
kusur sınıflarını yeniden görebildiğini kanıtlar. Python testleri otomatik
kapıdır; aşağıdaki case'ler kabul davranışını tarif eder.

## Case'ler

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 1 | `MT-GDK-001` | Temiz ağaç | `python3 scripts/kapi.py tarama` | Çıkış `0`; `Tarama: ✅ temiz` görünür |
| 2 | `MT-GDK-002` | `tests/` altında `Ornek 2.cs` dosyası | `python3 scripts/kapi.py tarama` | Çıkış `1`; dosya adı raporlanır. Dosya izlenmiş olsa da sonuç değişmez |
| 3 | `MT-GDK-003` | Temiz ağaç | `python3 scripts/kapi.py --komutlari-bas` | Hiçbir kapı koşmaz; dört .NET kapısı ve destek kapıları listelenir |
| 4 | `MT-GDK-004` | Derlenmiş `Tracon.Core.UnitTests` ikilisi | `python3 scripts/kapi.py test --sinif "*Capability*"` | Komut doğrudan test ikilisini ve `--filter-class` kullanır; `dotnet test --filter` kullanılmaz |
| 5 | `MT-GDK-005` | Git geçmişi erişilebilir | `python3 scripts/denetim-paketi.py --taban 7717ff1 --hedef 9b05f4b` | `RunEventWriter.cs` ve `Cost` imza-gövde adayı `ADAY` etiketiyle görünür; çıkış `0` |
| 6 | `MT-GDK-006` | Git geçmişi erişilebilir | Faz 68 ve Faz 73 SHA aralıklarını aynı komutla koş | Cache maliyeti ve iddiasız test adayları görünür; aday raporu kapıyı kırmaz |
| 7 | `MT-GDK-007` | Temiz ağaç | `python3 scripts/kapi.py kapanis --taban HEAD --site-atla` | Komutlar ucuzdan pahalıya koşar; ilk kırmızıdan sonra sonraki kapılar koşmaz |
| 8 | `MT-GDK-008` | Örnek uygulama build'i tamamlandı | Ardışık iki `dotnet build Tracon.slnx -c Release` koş | İkinci koşumda frontend kaynakları değişmediyse `npm run build` çalışmaz; `wwwroot` varlıkları yine pakete girer |
| 9 | `MT-GDK-009` | Faz 92 konsolidasyonu bitti | `python3 scripts/dokuman-bakim.py --denetle` | Çıkış `0`; kırık bağlantı `0` — `.agents/ortak/` bağlantıları dahil |
| 10 | `MT-GDK-010` | Faz 92 konsolidasyonu bitti | `wc -l -c .agents/skills/{faz-baslangic,faz-uygulama,faz-denetim,faz-tamamlama,tuketici-dokuman-senkronu}/SKILL.md .agents/skills/tuketici-dokuman-senkronu/resources/kalite-sozlesmesi.md` | Toplam, Faz 92 öncesi taban (1337 satır / 61.704 B) ile karşılaştırılır ve fazın kendi dokümanına yazılır |
| 11 | `MT-GDK-011` | 👤 insan gerekir — Claude Code'da skill listesi açık | Skill listesini gözle tara | On skill görünür (`aday-kesfi` · `faz-planlama` · `faz-baslangic` · `faz-uygulama` · `faz-denetim` · `faz-tamamlama` · `tuketici-dokuman-senkronu` · `maf-api-kesfi` · `kusur-giderme` · `manuel-test-kosumu`); `ortak` bir skill olarak **görünmez** |
| 12 | `MT-GDK-012` | Taze bağlamlı oturum | Yalnız `AGENTS.md` + `MEMORY.md` + bir faz dokümanı oku, sonra kapı komutunu bul | `.agents/ortak/kapilar.md` bağlantısını izleyerek `kapi.py kapanis` komutuna ve gerekçesine ulaşır — ham komut `AGENTS.md`'de tekrarlanmaz |
| 13 | `MT-GDK-013` | `docs/arsiv/fazlar/73-*.md` Faz 92'de düzeltildi | `git show <sha>:docs/73-TUKETICI-AGENT-DESTEGI.md \| head -5` (sha: `dokuman-bakim.py --denetle`'nin damıtılmış kayıt gerekçesindeki sha) | Tam metin hâlâ çözülür (K-598); düzeltme yalnız bugünkü dosyayı etkiler |
| 14 | `MT-GDK-014` | Temiz ağaç | `dotnet test tests/Tracon.Core.UnitTests -c Release --filter-class "*AmbientWriteSiteTests*\|*PlaywrightLocatorTests*"` (veya `./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests --filter-class "*AmbientWriteSiteTests*"` + `*PlaywrightLocatorTests*` ayrı ayrı) | Her iki sınıf da yeşil; `AmbientWriteSiteTests` 11 `<yol>:<metot>` girdisini taban çizgisiyle eşleştirir, `PlaywrightLocatorTests` `tests/Tracon.Ui.E2ETests/UiTests.cs` için taban çizgideki sayıyı dondurur |
| 15 | `MT-GDK-015` | Temiz ağaç | `src/` altında `async IAsyncEnumerable` döndüren bir metoda `TraconRunContext.SetCurrent(null);` ekle (Faz 93 kapısı), testi koştur, sonra geri al | `AmbientWriteSiteTests` **düşer**; hata mesajı eklenen `<dosya>:<metot>` yerini adıyla yazar |
| 16 | `MT-GDK-016` | Temiz ağaç | `tests/Tracon.Ui.E2ETests/` altında bir E2E dosyasına `Exact`/`.First`/`.Nth` taşımayan bir `GetByText("x")` çağrısı ekle (Faz 93 kapısı), testi koştur, sonra geri al | `PlaywrightLocatorTests` **düşer**; hata mesajı dosyayı ve yeni risk sayısını adıyla yazar |
| 17 | `MT-GDK-017` | Release assembly'leri üretildi | `cd docfx && dotnet docfx metadata docfx.json --logLevel warning`; sonra metadata girdisine geçici bir `"references": []` alanı ekleyip `DocfxConfigurationTests`'i koş ve değişikliği geri al | Metadata aynı birikmiş artifacts ağacında 0 warning/0 error ile biter; mutation testi düşer ve `artifacts/bin` globunun duplicate assembly yüklediğini açıklar |
| 18 | `MT-GDK-018` | Core unit test assembly'si üretildi | Canary ramp ve run heartbeat testlerindeki sonuç beklemelerini ayrı ayrı 1 ms sabit `Task.Delay` ile değiştir; sonra geri alıp `CanaryEvaluationServiceTests` ve `RunReconciliationTests` sınıflarını 10'ar kez koş | İki mutation kendi davranış iddiasında düşer; koşul tabanlı sürümler 50/50 ve 70/70 geçer. Sonuç yerine geçen sabit bekleme kalmaz |
| 19 | `MT-GDK-019` | Temiz ağaç | `python3 scripts/kapi.py performans` | Çıkış `0`; çıktı üç benchmark'ın (yazıcı, cache, `store` sorgusu) tahsis sayısını `bench/baseline.json`'a karşı ✅ olarak yazar |
| 20 | `MT-GDK-020` | `RunEventWriter.AppendAsync` içine kasıtlı bir `new List<int> { 1, 2, 3 }` eklendi | Aynı komut | Çıkış `1`; yalnız o benchmark ❌, hangi metot ve kaç bayt arttığı (`+72 B` gibi) adıyla yazılır; diğer iki benchmark ✅ kalır |
| 21 | `MT-GDK-021` | Bir benchmarkta tahsis kasıtlı azaltıldı (ör. gereksiz bir alan kaldırıldı) | Aynı komut | Çıkış `0`; o satır ❌ değil ⚠️'dır ve "taban çizgisi güncellenmeli" mesajını `--guncelle` bayrağıyla birlikte yazar |
| 22 | `MT-GDK-022` | `bench/baseline.json` silindi veya bozuldu (`{ geçersiz`) | Aynı komut | Çıkış `1`; anlaşılır bir `BaselineError` mesajı yazılır, ham Python traceback'i **görünmez** |
| 23 | `MT-GDK-023` | Sıcak yol dosyalarından (`RunEventWriter.cs`, `CompiledAgentCache.cs`, `SqlRunStore.cs`, `bench/`) hiçbiri `--taban` ile değişmedi | `python3 scripts/kapi.py kapanis --taban <sha>` | Performans adımı listede **hiç görünmez**; kapanış süresi artmaz |
| 24 | `MT-GDK-024` | 👤 insan gerekir — aynı commit iki farklı işletim sisteminde (ör. macOS ve Linux CI) | Her ikisinde `python3 scripts/kapi.py performans --guncelle` çalıştırıp `bench/baseline.json`'ı karşılaştır | Üç benchmark'ın `allocatedBytes` alanı **birebir aynı** — fazın "tahsis makineden bağımsızdır" temel varsayımı |
| 25 | `MT-GDK-025` | Temiz ağaç | `dotnet pack Tracon.slnx -c Release --no-build` | `Tracon.Benchmarks` için **hiçbir uyarı** yazılmaz (`IsPackable=false` + `WarnOnPackingNonPackableProject=false`); hiçbir `.nupkg` üretmez |

## Otomatik doğrulama

```bash
python3 -m unittest discover -s scripts -p "*_test.py"
python3 scripts/kapi.py tarama
python3 scripts/denetim-paketi.py --taban 7717ff1 --hedef 9b05f4b
```

`kapi.py`, komut sürelerini `artifacts/kapi-olcum.jsonl` dosyasına ekler.
`artifacts/` commit edilmez. `denetim-paketi.py` advisory'dir; ham diff'in
yerine geçmez ve regex adayları çıkış kodunu kırmaz.
