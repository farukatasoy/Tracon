# Faz 58 — Doküman Düzeni

> **Durum:** ✅ Tamamlandı (2026-08-16)
> **Kaynak:** Kullanıcı isteği (2026-08-15). Aday listesinde karşılığı **yoktur**.
> **Önkoşul:** Yok. **58.0 bölümü Faz 57'nin kapanışından önce yapılmalıdır** —
> gerekçe aşağıda.
> **Paketler:** Yok — bu faz koda dokunmaz · **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Değişmiyor

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/58-DOKUMAN-DUZENI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`docs/` 123 dosya, 5,7 MB, 102.049 satırdır. Disiplin iyi kurulmuş ama iki yerde çatlamıştır: 1. **Bütçe denetimi `docs/`'un yalnız %4'ünü görüyor.** Denetlenen dokuz dosyanın beşinin 30 bayttan az boşluğu kaldı; denetlenmeyen 5,5 MB sınırsız büyüyor. 2.

## Bitiş Ölçütleri (DoD)

- `python3 scripts/dokuman-bakim.py` çıkış kodu 0, **genişletilmiş** bütçe altında
- Bütçeli her dosyada en az %15 boşluk
- `docs/` toplam boyutu ölçüldü ve üst sınırın altında
- 58.2 tablosundaki altı yanlışın altısı düzeltildi
- README yol haritası Faz 0–59'u **tek tek** listeliyor
- Kendini "bayat/kullanılmaz" ilan eden üç bölüm kaldırıldı
- `grep -rn "00–32" README.md` boş döner
- Dört doğrulama kapısı sıfır uyarı verir (kod değişmese de koşulur)

### Doğrulama komutları

```bash
python3 scripts/dokuman-bakim.py; echo "çıkış: $?"
du -sh docs/
grep -rn "sekiz ekran\|00–32" README.md docs/       # boş dönmeli
```

---

## Plandan Sapmalar

### 🚨 S1 — Faz kapsamı dışı ama fazı bloke eden kusur: `main` derlenmiyordu

Faz 58'in ilk ölçümünde `HEAD` (`64882c6` "faz 57") **derlenmiyor** bulundu.
Senkronizasyon istemcisi iki test dosyasının Faz 57 ÖNCESİ Türkçe kopyasını
üretmiş ve kopyalar **commit edilmişti**:

| Dosya | Etki |
|---|---|
| `tests/AgentPrism.AspNetCore.FunctionalTests/GovernanceEndpointTests 2.cs` | `CS0101` — `GovernanceEndpointTests` aynı ad alanında iki kez |
| `tests/AgentPrism.PostgreSql.IntegrationTests/RunScoreStatisticsTests 2.cs` | aynı desen |

Kanıt tahminle değil **ölçümle** alındı: `HEAD`'te ayrı bir `git worktree`
kuruldu ve derlendi → `error CS0101`. Kopyalar ayrıca K-408'in çevirdiği
Türkçe kaynağı geri getiriyordu.

**Neden kaçtı — iki ayrı sebep:**
1. `git status` **temiz** görünüyordu; dosyalar izlenmeye başlandığı için
   "untracked" uyarısı vermiyordu.
2. `MEMORY.md`'deki tarama komutu yalnız `src`'ye bakıyordu; kopyalar
   `tests/` altındaydı.

**Yapılan:** kopyalar `git rm` ile kaldırıldı, `wwwroot` + frontend damgası
silindi. Tarama komutu `src tests samples` kapsayacak şekilde düzeltildi ve
**`faz-tamamlama` skill'ine kapılardan önce çalışan zorunlu bir adım olarak
eklendi** — bugüne dek yalnız `MEMORY.md`'de bir not olarak duruyordu, kapı
değildi. Kök sebep budur (K-411).

### S2 — 58.2'nin altı kaleminden üçü zaten doğruydu

Plan doğrulanmadan kabul edilmedi; her kalem ölçüldü:

| Plandaki iddia | Ölçüm | Sonuç |
|---|---|---|
| `README.md:351` "Faz dokümanları (00–32)" | Bugün `00–59` yazıyor | ✅ Zaten düzeltilmiş (58.0 not etmişti) |
| `MIMARI.md` "17 paket", tabloda 16 satır | `src`'de 18 `.csproj`, `Generators` `IsPackable=false` → **17**; `Sql.Shared`'ın `.csproj`'u **yok** (paylaşılan kaynak). README tablosu 17 satır ve hepsi gerçek paket | ✅ Sayı doğru; düzeltilecek bir şey yok |
| `Neden AgentPrism?` iki yerde | `MIMARI.md` bugün 5 satırlık **işaret**, kopya değil | ✅ Taşıma tamamlanmış |
| Test sayısı "dört farklı sayı" | Tek **güncel** iddia `README.md`'de (3355, bayat → **3695**). `AGENTS.md`/`MEMORY.md`'deki 1068 **tarihsel** ("Faz 20'de 1068 test kaçırdı") ve doğrudur | ⚠️ Kısmen: 1 düzeltildi, 2 tarihsel kayıt korundu |
| `README.md:166` "sekiz ekran" | Gerçek **27 ekran / 33 route** (`ls src/screens/*.tsx`, `app.tsx` route tablosu) | ✅ Düzeltildi |
| `manuel-test/03` 33 `☒` | Doğru; `☑` ile normalize edildi | ✅ Düzeltildi |

### S3 — Plan dışı bulunan ve düzeltilen ölçülebilir yanlışlar

| Yer | Yanlış | Doğru |
|---|---|---|
| `README.md:29` | "~159 KB gzip" | **165,8 KB** (`npm run build` kapısının kendi çıktısı) |
| `src/AgentPrism.UI/README.md` | "~88 KB" | 165,8 KB |
| `MIMARI.md:20` | PostgreSql `0001`–`0027`, SqlServer/Sqlite `0001`–`0014` | `0001`–**`0029`** · `0001`–**`0016`** |
| `MIMARI.md:17` | `Sql.Shared` "24 `store`" | **29** |
| `manuel-test/00-INDEKS.md` §4 ve §7 | "28 migration" | **29** |
| `KAPANIS-PLANI.md` §12 | `grep '[☒☑] Kaldı'` **satır** sayıyordu | Case bazında betik; yeniden koşulan case iki satır taşır |

### 🚨 S4 — `manuel-test` kapanışı "0 açık case" diyordu; gerçek **2**

58.3'ün ayrımı sayımı case bazında yapılabilir hâle getirdi ve kapanış
iddiasının yanlış olduğu ortaya çıktı:

| Case | Durum | Neden |
|---|---|---|
| `MT-UIRUN-019` | ☐ Beklemede | Playwright/CDP çevrimdışı emülasyonu açık SSE akışını kesmiyor; fiziksel ağ kesintisi ister |
| `MT-SKILL-057` | ⬜ Hiç koşulmadı | Koşum kaydı `_(koşum sırasında doldurulur)_` placeholder'ı ile bırakılmış |

Gerçek dağılım: **Geçti 1061 · Atlandı 29 · Kaldı 4 · Beklemede 1 ·
işaretsiz 1** (toplam 1096 koşum kaydı; `MT-SKILL-071` bilerek yazılmadı).
`Kaldı` 4'ü `KAPANIS-PLANI` §6'nın kalıcı listesiyle **birebir** uyuşuyor.
Sayıların bozulmadığı `HEAD` ile karşılaştırılarak doğrulandı: ayrım öncesi
ve sonrası satır sayıları **aynı**.

### S5 — `docs/` üst sınırı: plan kendisiyle çelişiyordu (kullanıcı kararı)

Açık Soru 3 "4 MB (58.1 sonrası hedef)" öneriyordu; gerekçesi "58.1 ~600 KB
siler" idi. Ama 58.1 ve 58.3 **silmez, taşır** (`AGENTS.md` kuralı) ve
hedefler `docs/` içindedir. Net etki **−12 KB**. Ölçüm kullanıcıya sunuldu;
karar: **sınır `docs/arsiv/` ve `docs/manuel-test/kosumlar/` HARİÇ ölçülür**
(K-412). Böylece arşive taşımak sayacı gerçekten düşürür.

### S6 — README yol haritası ayrı dosyaya taşındı ve ÜRETİLİR oldu

DoD "README yol haritası Faz 0–59'u tek tek listeliyor" istiyordu; 36 satır
eklemek README'yi 20 KB bütçesinden taşırırdı. Planın risk tablosundaki
kaçış yolu uygulandı ("gerekirse yol haritası ayrı dosyaya taşınır") — ama
bir adım ileri gidildi: `docs/YOL-HARITASI.md` **elle yazılmaz**, her fazın
kendi `> **Durum:**` satırından üretilir (K-413). Faz 58'in düzelttiği kayma
sınıfı böylece yapısal olarak imkânsızlaşır.

---

## Bu Fazda Verilen Kararlar

| K | Karar |
|---|---|
| **K-411** | Senkronizasyon kopyası taraması `faz-tamamlama`'ya **kapı** olarak eklendi; kapsam `src tests samples`. Not olmak yetmedi — Faz 57 `main`'i kırdı |
| **K-412** | Doküman bütçesi dizin sınırları `docs/arsiv/` ve `docs/manuel-test/kosumlar/` **hariç** ölçülür (kullanıcı kararı) |
| **K-413** | `docs/YOL-HARITASI.md` üretilen dosyadır; kaynak her fazın `Durum:` satırıdır |
| **K-414** | Koşum kaydı ile spesifikasyon ayrı dosyalarda yaşar; ikinci koşum kardeş `kosumlar/<tarih>/` açar, üzerine yazmaz |

## Ölçülen Sonuç

| Kapı | Sonuç |
|---|---|
| `dotnet build` | ✅ 0 uyarı, 0 hata |
| `dotnet test` | ✅ **3695** test, 16 proje, 0 gerçek başarısız |
| `dotnet pack` | ✅ çıkış 0, 17 paket |
| `dotnet format` | ✅ çıkış 0 |
| `dokuman-bakim.py` | ✅ çıkış 0, **her bütçede ≥%15 boşluk** |

> ⚠️ `Templates.Tests` tam pakette bir kez `ExitCode 70` ile düştü
> (`~/.templateengine/packages.json` kilit çakışması), izole koşumda 10/10
> geçti. Ürün kusuru değildir; ayırt etme yordamı
> `docs/hafiza/test-altyapisi.md`'ye yazıldı.

**Denetlenen `docs/` boyutu:** 4.212.753 B / 5.000.000 B sınır (%16 boş).
Denetim dışı arşiv + koşum kaydı: 1.273.537 B.

## Sonraki Faza Devir Notu

Sıradaki faz: **[Faz 59 — Ürün Dokümantasyonu](59-URUN-DOKUMANTASYONU.md)**.

**Faz 59'un bilmesi gerekenler:**

1. **Yol haritası artık üretilir.** Bir fazın durumunu değiştirmek için
   `docs/NN-*.md`'nin `> **Durum:**` satırını düzelt, sonra
   `python3 scripts/dokuman-bakim.py` çalıştır. `YOL-HARITASI.md`'yi elle
   düzenlemek bir sonraki üretimde geri alınır.
2. **Bütçe artık dizin de denetliyor.** Faz 59 bir doküman sitesi kuracaksa
   üretilen HTML/statik varlıklar `docs/` altına konursa sınırı zorlar.
   Öneri: site çıktısı `docs/` **dışına** (`site/` veya `artifacts/`) yazılsın;
   gerekirse `HARIC` listesine eklensin.
3. **Açık iki manuel test case'i vardır** (S4). Faz 59 "ürün hazır"
   anlatısı kuracaksa bu iki kalem ya koşulmalı ya da bilinen sınır olarak
   yazılmalıdır.
4. **`docs/` Türkçe kalır** (kullanıcı kararı, 2026-08-15). Faz 59 ürün
   dokümantasyonu **İngilizce** olacaksa bu ayrı bir karardır ve
   sorulmalıdır — K-408'in sınırı `docs/`'u kapsam dışı bırakır.
5. **`manuel-test/` spec dosyaları yeniden koşulabilir durumdadır.** İkinci
   koşum `kosumlar/<yeni-tarih>/` açar; 2026-08-13 kaydını ezmez. Spec'teki
   donmuş sayılar (`03`'teki "28 migration") uyarı notuyla işaretlendi.
