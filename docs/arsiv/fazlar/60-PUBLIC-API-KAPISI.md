# Faz 60 — Public API Kapısı

> **Durum:** ✅ Tamamlandı (2026-08-16)
> **Kaynak:** Kullanıcı kararı, 2026-08-16 — süreç iyileştirme oturumu. Aday listesinden gelmez.
> **Önkoşul:** Yok. [Faz 7](07-SAGLAMLASTIRMA-VE-YAYIN.md) **beklemez** — bu faz yayın kararından bağımsızdır.
> **Paketler:** Yayınlanan 17 paketin tamamı
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — **daralıyor**. 10 metodun aşırı yükleme çifti sadeleşir (kırıcı; bugün bedava, yayından sonra pahalı)
> **Site etkisi:** `packages.md` (API kararlılığı vaadi) · `api/` üretilir, elle yazılmaz
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) (`PKG`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/60-PUBLIC-API-KAPISI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism 17 NuGet paketi yayınlayacak bir kütüphane ailesidir. Bir tüketici için **kırıcı API değişikliği bir üretim kusurudur** — kodun kendisi doğru olsa bile. Bugün böyle bir değişikliği hiçbir kapı yakalamaz: `EnablePublicApiTracking` `false`'tur ve dokuz `RS` kuralı `NoWarn` listesindedir. Bu faz kapıyı kurar.

## Bitiş Ölçütleri (DoD)

- [x] `EnablePublicApiTracking` `true`; `NoWarn` koşullu satırı silindi
- [x] `PublicAPI.Unshipped.txt` 16 pakette dolu (meta `AgentPrism` kendi derlenmiş üyesi olmadığı için boş — beklenen); 17. paket olan `Templates` K-424 gereği dosya almıyor (kasıtlı hariç tutma); `Shipped.txt` boş
- [x] `RS0026`'nın 10 kalemi kapandı; her biri için seçim ve gerekçe dokümanda (K-422, "Plandan Sapmalar" §4)
- [x] `RS0041` ya kapandı ya gerekçesiyle **tek başına** bastırıldı (karar defterinde — K-423)
- [x] Manuel case 2: yeni bir public üye derlemeyi **kırıyor** (gerçek çıktı yazıldı — `MT-PKG-091`)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `PKG` alanına eklendi; otomatikleştirilebilenler koşuldu (`MT-PKG-090..093`, dördü de koşuldu)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/packages.md` API kararlılığı vaadini anlatıyor

### Doğrulama komutları

```bash
# Kapı gerçekten kapalı mı: kayıtsız üye derlemeyi kırmalı
dotnet build AgentPrism.slnx -c Release 2>&1 | grep -c "RS0016"   # 0 beklenir

# Sonra bir public üye ekleyip tekrar: sıfırdan büyük olmalı
```

---

## Plandan Sapmalar

1. **Ölçülen taban çizgisi plandan büyüktü.** Plan 5.052 `RS0016` bekliyordu
   (2026-08-16'da ölçülmüştü); uygulama anında (aynı gün, Faz 58/59 sonrası)
   6.792 çıktı. Neden: aradan iki faz geçmiş, kod büyümüştü. Mekanizma
   (`dotnet format analyzers` toplu düzeltmesi) sayıdan bağımsız çalıştığı için
   plan değişmedi.
2. **`dotnet format analyzers` tek koşumda bitmedi — 13 iterasyon gerekti.**
   Plan bunu "uygulama anında ölçülmeli" diye işaretlemişti (doğrulanmadı).
   Ölçüldü: her koşum yalnız BİR sonraki projenin diagnostiklerini çözüyor,
   çözüm bağımlılık sırasına yakın ilerliyor. Yedek betik gerekmedi.
3. **`Generators`/`Templates`'in dosya eklenmemesi YETMEDİ.** Plan bu ikisini
   "yalnız `PublicAPI.txt` eklenmez" diyerek hariç tutmayı öngörüyordu; ölçüm
   `src/Directory.Build.props`'taki analyzer referansının KOŞULSUZ olduğunu
   ve `Generators`'ın gerçekten `RS0016` ürettiğini gösterdi — bu da toplu
   düzeltmenin `System.NotSupportedException` ile çökmesine sebep oldu (K-424).
   Gerçek bir MSBuild özelliği eklendi.
4. **RS0026'nın çözümü tek strateji değil, dört aileydi.** Plan üç seçenek
   sundu (birleştir/ayrıştır/yeniden adlandır) ama uygulama sırasında RS0027
   ("opsiyonel parametre en uzun aşırı yüklemede olmalı") kuralı ölçüldü ve
   ilk denemeleri (kısa aşırı yüklemede bırakmak) kırdı. Ayrıntı: K-422.
5. **RS0041'in kök nedeni doğrulandı ve tek satırlık bastırma yeterli çıktı**
   — plan "çözülemezse bastırılabilir" diyordu; kök neden (System.Text.Json
   kaynak üreteci) elle düzeltilemeyecek türden çıktı, bastırma uygulandı.
6. **`UseMcp`'nin ikinci aşırı yüklemesi yeniden ADLANDIRILMADI.** Plan
   "muhtemelen (3) uygundur [rename]" diye öngörmüştü; ölçüm gösterdi ki
   yalnız `configure` parametresinin varsayılanını kaldırmak (isim aynı
   kalarak) RS0026/27'yi temizliyor VE K-353'ün "tek çağrıda config+kod"
   gerekçesini bozmuyor — rename gereksiz kapsam büyümesiydi.

## Bu Fazda Verilen Kararlar

- **K-421** — Public API takibi Faz 60'ta, yayından bağımsız açıldı (K-016/K-068'i günceller).
- **K-422** — RS0026'nın 10 kalemi dört stratejiyle çözüldü (birleştir · varsayılan kaldır · sıfır-opsiyonel üçlü bölünme · `private` ctor + `FromClient`).
- **K-423** — RS0041, tek kaynağa (üretilmiş `WebhookEventPayloadJsonContext`) izlenip `AgentPrism.Abstractions.csproj`'da tek satırla bastırıldı.
- **K-424** — `Generators`/`Templates`, yeni `AgentPrismPublicApiTrackingEnabled` MSBuild özelliğiyle takipten hariç tutuldu.

Tam gerekçeler: `docs/KARARLAR.md`, K-421 – K-424.

## Denetim Bulguları

İki bağımsız, taze bağlamlı denetçi çalıştırıldı (biri izole `worktree`'de, biri
gerçek çalışma ağacına karşı). Her ikisi de dört kapıyı ve DoD'nin her satırını
bağımsızca yeniden koştu/doğruladı.

**🔴 (kapanmadan faz bitmez):** Yok — her iki denetçide de.

**🟡 (aynı fazda kapanır):** Birinci denetçi üç bulgu verdi, hepsi kapatıldı:
1. `docs/KARARLAR.md`'deki K-016/K-068 satırları K-421'in çelişen eski iddiasını
   taşıyordu → her ikisine de "(yeniden açıldı: 2026-08-16, K-421)" notu eklendi.
2. `docs-site/getting-started/tools.md`'deki `AddTool(AIFunctionFactory.Create(...))`
   örneği artık derlenmiyordu (`requiresApproval`'ın varsayılanı kaldırıldığı
   için) → `requiresApproval: false` eklenerek düzeltildi. (İkinci denetçi bu
   dosyayı 🟢 olarak ayrıca doğruladı.)
3. `docs/KARARLAR-INDEKS.md` K-421–424'ü henüz içermiyordu → bulgu zaten
   bayattı; `python3 scripts/dokuman-bakim.py` bu bulgudan önce koşulmuştu.

**🟢 (aday/not, kapatmayı gerektirmez):**
1. `docs-site/packages.md`/`concepts/index.md`/`getting-started/index.md`
   diff'inde Faz 60'ın kapsamı dışında (`UseOpenAICompatible`/self-hosted model
   anlatımı) içerik var. Doğrulandı: bu içerik bu **oturumdan önce**, çalışma
   ağacında commit edilmemiş olarak zaten duruyordu (bu fazın konusu değil,
   dokunulmadı — birinci denetçinin `worktree` izolasyonu bunu ayırt edemedi).
2. İkinci denetçi, bu diff'in dışında, `QuotaUsageObserverTests`'te
   (`AgentPrism.Core.UnitTests`, son değişikliği Faz 57) ara sıra görülen bir
   `ObjectDisposedException` (SemaphoreSlim) yarışını gözlemledi — iki yeniden
   koşumda geçti. Faz 60'ın diff'inde yok, bu fazı bloklamaz; ayrı bir kusur
   olarak `docs/ADAYLAR.md`'ye değil, doğrudan bir sonraki
   `kusur-giderme` oturumuna bırakılır.

Denetim sonrası dört kapı yeniden koşuldu: `dotnet build` 0/0, `dotnet format
--verify-no-changes` exit 0, `dotnet pack` 17 paket.

## Sonraki Faza Devir Notu

🚨 **Faz 7'ye (yayın) devredilecek tek satır:** `PublicAPI.Unshipped.txt` →
`PublicAPI.Shipped.txt` taşıması yayın anında, tek seferlik ve mekanik bir
adımdır — bugün `Shipped.txt` her pakette bilerek boş bırakıldı (K-421).

Diğer notlar:
- `AgentPrismPublicApiTrackingEnabled` özelliği yalnız `Generators`/`Templates`'te
  `false`; yeni bir paket eklenirse varsayılan (`true`) otomatik uygulanır —
  hariç tutma gerekiyorsa bilinçli eklenmeli.
- `UseMcp`'nin `IConfiguration` aşırı yüklemesi artık `configure`'ı zorunlu
  ister (nullable tip korunur, `null` geçilebilir). Yeni bir `Use*` uzantısı
  yazarken bu üçlü deseni (bare / `Action<T>` zorunlu / `IConfiguration`)
  örnek al — RS0026/27'yi baştan önler.
