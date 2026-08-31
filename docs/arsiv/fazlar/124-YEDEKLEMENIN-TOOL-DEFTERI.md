# Faz 124 — Yedeklemenin Tool Defteri

> **Durum:** ✅ Tamamlandı (2026-08-31)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](../../kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **K-1** (kusur kanalından faz kanalına geçti)
> **Önkoşul:** [Faz 62](62-MODEL-YEDEK-ZINCIRI-VE-ON-UCUS-DENETIMI.md) (yedek zinciri) ve [Faz 87](87-KESILEN-ISIN-DEVAMI.md) (kesinti devamı, `RecordedToolPlayback`) — ikisi de arşivde; yalnız aşağıdaki grep'lerle okunur
> **Paketler:** `AgentPrism.Core` (`Models/FallbackChatClient.cs`, `Replay/RecordedToolPlayback.cs`)
> **Yeni paket:** Yok · **Migration:** Yok — defter turun ömrü kadar yaşar, hiçbir yere yazılmaz
> **Public API:** Büyümüyor. Dokunulan iki tip de `internal`. Bu, fazın en ucuz tarafıdır: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır (K-603) ve bu faz o sayıya bir satır bile eklemez
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/reliability.md` (yedekleme bölümü), `concepts/tools.md` · sevk edilen: `FallbackChatClient` XML `<remarks>`'ı — 🚨 bugün **yanlış** bir davranışı doğru diye ilan ediyor
> **Manuel test alanı:** `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show abf8a8a:docs/arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md
> ```
>
> Damıtıldı 2026-08-31 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir sağlayıcı yedeklemesi, birincil sağlayıcıda **zaten çalışmış** bir tool'u yedek sağlayıcıda ikinci kez çalıştırabilir. Ödeme alan, kayıt silen veya webhook atan bir tool bu yolda iki kez çalışır ve bunu kimse görmez.

## Bitiş Ölçütleri (DoD)

- [x] `ToolEffect.External` + `SafeToRepeat=false` bir tool, yedeğe geçen akışsız bir run'da **bir kez** çalışır — `FallbackToolSideEffectTests` sayacı `1` gösterir
- [x] Yedek modelin defterde olmayan çağrısı gerçekten çalışır — aynı testte ikinci sayaç artar
- [x] Üç bağlantılı zincirde ikinci bağlantının tool'u üçüncüde tekrar çalışmaz
- [x] Yedeklemesiz run'da hiçbir davranış değişmez; `RepeatableToolContract` ve replay testleri değişmeden geçer
- [x] Sınıf taraması yapıldı; altı yolun her biri için sonuç bu dokümana yazıldı (kapalı / düzeltildi / gerekçeyle devredildi)
- [x] `FallbackChatClient` XML `<remarks>`'ı gerçek davranışı anlatıyor
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` yerine tam DI + HTTP + gerçek `FunctionInvokingChatClient` üzerinden koşan `FallbackToolSideEffectTests` ile aynı kanıt elde edildi (bkz. Plandan Sapmalar)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` içine eklendi (MT-MYU-017); otomatikleştirilmiş kanıta yönlendirildi (MT-MYU-014 emsali)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Defterin gerçekten devrede olduğu
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests --filter-method "*FallbackToolLedger*"

# Sınırı geçen davranış
./artifacts/bin/AgentPrism.AspNetCore.FunctionalTests/release/AgentPrism.AspNetCore.FunctionalTests --filter-method "*FallbackToolSideEffect*"

# Kapılar
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>
```

---

## Plandan Sapmalar

- **`RecordedToolPlayback`'in ledger'ı sahiplik (owner) etiketi taşıyor —
  planda yoktu, bağımsız denetimde bulundu.** Plan yalnız `(ad, argüman)`
  anahtarına göre eşleştirme öngörüyordu (mevcut `RecordedToolPlayback`
  deseninin birebir uzantısı). Uygulama sırasında bu, **tek bir bağlantının
  kendi iç tool döngüsünde** aynı tool'u aynı argümanla iki kez çağırmasını
  da (hiç yedeğe geçilmeden) yanlışlıkla tekilleştiriyordu — ikinci çağrı
  gövdeyi hiç çalıştırmadan ilkinin sonucunu döndürüyordu. Düzeltme: her
  `PlaybackFunction` sarmalayıcı örneği kendi kimliğini yazdığı kayda damgalar
  (`LedgerEntry.Owner`); `Take` bir kaydı yalnız FARKLI bir sahibe aitse
  eşleştirir. Bir bağlantı kendi yazdığını asla geri okumaz; yalnız bir
  ÖNCEKİ (tamamlanmış) bağlantının yazdığı bir kayıt bir SONRAKİ bağlantıya
  servis edilebilir. Bağlantılar sırayla çalıştığı için (`FallbackChatClient`'ın
  `for` döngüsü bir sonrakine geçmeden önce öncekini tam bekler) bu, aynı
  anahtar için kuyruktaki girdilerin **her zaman bağlantı sırasına göre
  bloklar hâlinde** durduğu anlamına gelir — kendi sahibine ait bir girdi
  görüldüğü an kuyrukta borç alınacak başka bir şey kalmadığı kanıtlanır.
  Kanıt: `FallbackToolLedgerTests.The_same_wrapper_asking_twice_never_answers_itself_from_the_ledger`,
  `.A_primary_that_never_falls_back_still_runs_a_repeated_identical_call_twice`,
  `FallbackToolSideEffectTests.A_primary_that_never_fails_over_still_runs_a_repeated_identical_call_twice`
  (gerçek HTTP + DI + `FunctionInvokingChatClient`).
- **Örnek uygulama (`samples/AgentPrism.Api`) yerine gerçek HTTP+DI+
  `FunctionInvokingChatClient` üzerinden koşan fonksiyonel testler kullanıldı**
  (DoD'nin ilgili satırı buna göre güncellendi). Gerekçe: bu fazın kanıtlaması
  gereken tam senaryo (bir tool zaten çalıştıktan **hemen sonra ve tam o
  turda** sağlayıcının çökmesi) gerçek bir LLM ile zorlanamaz — MT-MYU-014'ün
  eşzamanlı tool çağrısı için verdiği gerekçenin birebir aynısı. Sahte
  sağlayıcı çifti (`StepModelProvider`) + gerçek `[AgentPrismTool]` gövdesi +
  gerçek `FunctionInvokingChatClient` bunun yerine **her koşumda garantili**
  bir kanıt üretir.
- **İki konuyla ilgisiz kusur da bu oturumda düzeltildi** (kapanış kapısı
  koşulurken bulundu, bu fazın kodunu hiç etkilemez):
  1. `RecordedToolPlayback.cs` ve `AgentPrismResponseCachingChatClient.cs`
     içinde birer karakter literaline yanlışlıkla gömülü NUL (`U+0000`) baytı
     — git bu iki dosyayı "binary" sanıyordu (`git diff` "Bin X -> Y bytes"
     gösteriyordu). Düzeltme: baytı düz boşluk karakteriyle değiştirmek;
     davranış değişmedi (okuma ve yazma yolu zaten aynı ayırıcıyı
     kullanıyordu). Ders `docs/hafiza/build-ve-analyzer.md`'ye yazıldı.
  2. `scripts/applied-migrations.json`'da HEAD'deki (`20c9732`) üç yeni
     `session_version` migration dosyası için eksik `sourceCommits` girdileri
     — `scripts/kapi.py tarama` bunlar olmadan başarısız oluyordu. Girdiler o
     dosyaların gerçek kaynak commit'ine (`20c9732`) eklendi.
- **`docs/manuel-test/00-INDEKS.md`'deki dosya 27 case sayısı (14) gerçek
  sayıyla (bu fazdan önce bile 16'ydı) uyumsuzdu** — bu fazın MT-MYU-017'yi
  eklemesiyle sayı 17'ye çıktı; index satırı düzeltildi, "İlgili faz" sütununa
  113 ve 124 eklendi (yalnız 62/81 listeleniyordu).

## Bu Fazda Verilen Kararlar

Yok — bu fazın tek değişikliği internal implementation detayı (`RecordedToolPlayback`'in
sahiplik etiketi dahil). Public API, compatibility contract, güvenlik/kiracı
sınırı veya kalıcı veri sözleşmesi büyümedi; yeni bir `K-NNN` gerekmiyor.

## Denetim Bulguları

İki bağımsız denetim koşuldu (ikincisi, ilkinin worktree izolasyonu nedeniyle
diff'i göremediği ölçülünce, ana çalışma ağacında tekrar koşuldu — ikisi de
rapor üretti, aşağıda ikisi de kayıtlı).

| # | Bulgu | Denetim | Seviye | Sonuç |
|---|---|---|---|---|
| 1 | Ledger bağlantı 0'ı (birincil) da sarmalıyor; hiç yedeğe geçilmeden, TEK bir bağlantının kendi iç tool döngüsünde aynı tool'un aynı argümanla ikinci çağrısı deftere serviyordu (gövde ikinci kez çalışmıyordu) | Denetim #1: 🔴 · Denetim #2: 🟡 (aynı bulgu, farklı seviye) | 🔴 (daha ihtiyatlı sınıflandırma kabul edildi) | **Düzeltildi** — `RecordedToolPlayback`'e sahiplik (owner) etiketi eklendi; bir sarmalayıcı kendi yazdığını asla geri okumaz. Regresyon: `FallbackToolLedgerTests.The_same_wrapper_asking_twice_never_answers_itself_from_the_ledger`, `.A_primary_that_never_falls_back_still_runs_a_repeated_identical_call_twice`, `FallbackToolSideEffectTests.A_primary_that_never_fails_over_still_runs_a_repeated_identical_call_twice` |
| 2 | Sıfır argümanlı bir tool çağrısı (`FormatArguments` `null` döner) hiçbir testte yok, plan bunu vaat ediyordu | Denetim #2 | 🟡 | **Düzeltildi** — `FallbackToolLedgerTests.A_zero_argument_call_is_recorded_and_answered_from_the_ledger_across_wrappers` eklendi |
| 3 | `AllowConcurrentToolCalls=true` iken AYNI tool'un AYNI argümanla TAM eşzamanlı iki çağrısı, ikisi de "kayıt yok" görüp ikisi de canlı çalışabilir (check-then-act) | Denetim #1 | 🟡 | **Gerekçelendi, kapatılmadı** — bu, ledger'ın VAAT ETTİĞİ şeyin (tamamlanmış bir çağrının tekrarını önlemek) dışında bir garanti: hiçbir şey ÖNCEDEN kaydedilmemişken iki eşzamanlı çağrı, ledger hiç olmasaydı da AYNI şekilde ikisi de çalışırdı — bu davranışı "düzeltmek" istek bağlantı içi çağrı tekilleştirmesi (deduplication/coalescing) eklemek olur, bu fazın kapsamı DEĞİL. Bulgu #1'in düzeltmesi bu senaryoyu KISMEN de güçlendirdi: `ConcurrentQueue.TryDequeue`'nun atomikliği, bir ÖNCEKİ bağlantıdan BORÇ ALINAN tek bir girdiyi iki eşzamanlı istekten yalnız birine verir, diğeri güvenle canlı çalışır — çökme veya çift-servis riski yok |
| 4 | `docs/manuel-test/00-INDEKS.md`'de dosya 27 case sayısı bu fazdan ÖNCE bile yanlıştı (14 vs gerçek 16), bu faz onu büyüttü (17) | Denetim #1 | 🟢 | **Düzeltildi** (kapsam dışı olsa da ucuzdu) — index satırı düzeltildi |
| 5 | Tek bir NUL baytının git'i "binary" sanmaya ittiği tuzak hiçbir hafıza dosyasında kayıtlı değildi | Denetim #2 | 🟢 | **Kaydedildi** — `docs/hafiza/build-ve-analyzer.md` |

**Temiz çıkan başlıklar (her iki denetimde):** 3.1 (DoD), 3.2 (test tiyatrosu
yok), 3.3 (test seviyesi doğru — sınırı geçen davranış fonksiyonel test
edilmiş), 3.5 (imza-gövde kayması yok), 3.6 (plan dışı public API yok), 3.7
(repo kuralları), 3.8 (dört doğrulama kapısı gerçekten koşuldu).

Bulgu 1 ve 2 kapandıktan sonra `dotnet build` + `AgentPrism.Core.UnitTests`
(2144/2144) + `AgentPrism.AspNetCore.FunctionalTests` (703/703) yeniden
koşuldu; hepsi yeşil.

## Sonraki Faza Devir Notu

- **`RecordedToolPlayback`'in sahiplik (owner) deseni, `recordLiveCalls`
  kullanan gelecekteki her genişleme için emsaldir.** Bir ledger tasarımı
  "aynı anahtar iki kez görülürse ikincisini öncekinden cevapla" diyorsa,
  önce "hangi ÇAĞIRAN kendi yazdığını geri okuyabilir" sorusunu sor —
  `Wrap()` her çağrıldığında yeni bir kimlik üretir, bu yüzden "hangi
  bağlantı/tur" sorusu ek bir parametre plumbing'i gerektirmeden bu kimlikle
  cevaplanabilir.
- **Bir plan diyagramının kendi şekli bile yanlış olabilir.** Bu fazın kendi
  akış diyagramı (124.1) bağlantı 0'ın da defterle sarmalandığını doğru
  gösteriyordu ama "aynı bağlantı kendi içinde tekrar sorarsa ne olur"
  sorusunu görsel olarak ayırt etmiyordu — `faz-uygulama`'nın "planın yapısal
  iddiasını ölçmeden kabul etme" kuralı burada da geçerliydi, yalnız kod
  DEĞİL diyagram/plan seviyesinde.
- Workflow düğüm retry'ının (`WorkflowNodeRetryPolicy`) agent run'ı
  taşımadığı ölçüldü (§ 124.3) — bu alanda YENİ bir agent-run-taşıyan retry
  yolu eklenirse (ör. `AddWorkflowFunction`'ın kendisi agent'ları
  sarmalayacak şekilde genişlerse), sınıf taraması yeniden açılmalıdır.
