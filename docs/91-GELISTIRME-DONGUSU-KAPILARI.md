# Faz 91 — Geliştirme Döngüsü Kapıları

> **Durum:** 📋 Planlandı (2026-08-23)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) kalem **3** (kısmi) ve kalem **20** · kullanıcı isteği: geliştirme sürecinin uçtan uca optimizasyonu. Bu faz bir `F-NN` adayından gelmez; envanter turunun iki kalemini birleştirir.
> **Önkoşul:** Yok
> **Paketler:** Yok — iş `scripts/`, `.github/`, `src/AgentPrism.UI/*.targets` üzerindedir
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `PublicAPI.Unshipped.txt` 8079 satır, `Shipped.txt` dosyaları boş (16 dosya × 1 satır); bu faz ikisine de dokunmaz
> **Tüketici yüzeyi:** **Yok.** `tuketici-dokuman-senkronu` Adım 0 tablosundaki hiçbir yol tutmuyor: public üye değişmiyor, HTTP ucu yok, ekran yok, yeni paket yok. `AgentPrism.UI.Frontend.targets` yalnız `AgentPrism.UI.csproj:61` tarafından import edilir; `buildTransitive/` içinde **değildir**, tüketiciye gitmez. Skill koşmaz — gerekçe budur
> **Manuel test alanı:** [`docs/manuel-test/33-DOKUMAN-KAPILARI.md`](manuel-test/33-DOKUMAN-KAPILARI.md) (Adım 4 kapıları) + yeni aile `36-GELISTIRME-KAPILARI.md` (`kapi.py`, `denetim-paketi.py`)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Bu liste o skill'in 2. adımıdır — **tamamını
> değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-413\|K-421\|K-598\|K-599" docs/KARARLAR.md
   ```
   **K-413** (`YOL-HARITASI.md` üretilir, elle yazılmaz) · **K-421**
   (`EnablePublicApiTracking` açık) · **K-598** (tam metin git'te yaşar,
   çözülebilirliği her denetimde kanıtlanır) · **K-599** (her ağaç kendi
   bütçesini alır)
3. [`docs/arsiv/fazlar/90-DOKUMAN-DAMITMA-POLITIKASI.md`](arsiv/fazlar/90-DOKUMAN-DAMITMA-POLITIKASI.md)
   — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/90-DOKUMAN-DAMITMA-POLITIKASI.md
   ```
   Faz 90 `dokuman-bakim.py`'ye üç kapı ekledi (üretilen dosya tazeliği, karar
   gerekçesi işaretçisi, damıtılmış kayıt tam metni). Bu faz aynı dosyaya üç
   kapı daha ekler; o üçünün deseni izlenir.
4. Alan hafızası — bu faz üç alana dokunuyor:
   [`hafiza/test-kosum-tuzaklari.md`](hafiza/test-kosum-tuzaklari.md) (MTP filtre
   biçimi, asılma, kırılganlık) ·
   [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md) (MSBuild target
   davranışı) · [`hafiza/dokumantasyon.md`](hafiza/dokumantasyon.md) (doküman
   kapısı yazma tuzakları)
5. Gerektiğinde: [`.agents/skills/faz-tamamlama/SKILL.md`](../.agents/skills/faz-tamamlama/SKILL.md)
   Adım 1 ve Adım 7 — bu fazın devraldığı komutlar oradadır

---

## Amaç

Bu faz, geliştirme döngüsünün **prose ile taşınan** kalite kurallarını makine
kapılarına çevirir ve döngünün maliyetini ilk kez ölçer.

Bugün bir fazın kalite kuralları çoğunlukla metin olarak yaşıyor. Metin iki
bedel ödetir: her fazda yeniden bağlama yüklenir (token) ve her fazda agent'ın
uyma iradesine bağlı kalır (kalite). Repo doğru ilkeyi zaten yazmış ama
**reaktif** uyguluyor:

> "Bir kusur sınıfı **üçüncü** kez tekrarlıyorsa yazı yetmemiştir: o zaman kapı
> gerekir (test, analyzer kuralı veya `scripts/` denetimi)."
> — [`kusur-giderme/SKILL.md:128`](../.agents/skills/kusur-giderme/SKILL.md)

Bu faz o ilkeyi varsayılan yapar. Tek cümlelik tez: **bir kural prose'dan koda
taşındığında token maliyeti sıfıra, güvenilirliği tam güvene gider.**

- **Envanter kalem 3 (kısmi)** — kanıtlanmış kusur sınıflarından *script ile*
  yakalanabilenler kapıya döner. Roslyn analyzer kuralları kapsam **dışıdır**
  (aşağıya bakın).
- **Envanter kalem 20** — bakım aparatının kendisi bakım ister hâle geldi; bu
  faz aparata **yeni yüzey eklemez**, var olan komutları tek yerde toplar.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| Ölçüm yok | Tam `dotnet test` süresi, CI süresi ve kapanış kapısı toplam maliyeti repoda **hiçbir yerde kayıtlı değil**. "Hızlandırdık" iddiası bugün kanıtlanamaz |
| [`hafiza/test-kosum-tuzaklari.md:23-26`](hafiza/test-kosum-tuzaklari.md) | `dotnet test --filter` MTP'de sessizce yutulur; **1004 testin tamamı** koşar ve yeşil döner. "Tehlike yeşil bir yanlıştır" |
| [`ci.yml:42`](../.github/workflows/ci.yml) + [`faz-tamamlama/SKILL.md:44`](../.agents/skills/faz-tamamlama/SKILL.md) | Senkronizasyon kopyası `find` deseni **iki yerde** kopyalanmış |
| [`ci.yml:61`](../.github/workflows/ci.yml) + [`faz-tamamlama/SKILL.md:66`](../.agents/skills/faz-tamamlama/SKILL.md) | `secret` regex'i **iki yerde** kopyalanmış; birinde değişirse diğeri bayatlar |
| [`Frontend.targets:60`](../src/AgentPrism.UI/AgentPrism.UI.Frontend.targets) | `AgentPrismDetectNode`'un `Inputs`/`Outputs` çifti **yok**; `DependsOnTargets` üzerinden dış build + üç iç build'de tetiklenir |
| [`Frontend.targets:169`](../src/AgentPrism.UI/AgentPrism.UI.Frontend.targets) | `AgentPrismCollectFrontendAssets` de `Inputs`/`Outputs` taşımaz; her iç build'de `wwwroot/**/*` glob'lanır |
| [`faz-denetim/SKILL.md:114`](../.agents/skills/faz-denetim/SKILL.md) ↔ [`Directory.Build.props:58`](../Directory.Build.props) | Skill "`EnablePublicApiTracking` bugün `false`" diyor; `.props` `true` diyor. Skill **bayat** |
| `docs/arsiv/fazlar/*.md` | `✅ Tamamlandı` işaretli **8 fazda 28 işaretsiz `- [ ]` kutusu**. En ağırı `71-WORKFLOW-KOD-DUGUMU.md`: 14 kutu, `- [ ] faz-denetim koşuldu` dahil — oysa aynı dosya satır 149'da "🔴 yok" diyor |
| `cekirdek-calistirma.md:18` · `faz-uygulama/SKILL.md:124` · `AGENTS.md:193` | Aynı kusur sınıfının sayacı üç dosyada **üç farklı değer**: "uc vaka" · "dört kez" · "Beş kez" |
| `tests/` geneli | `[Trait]` sayısı **0** — kategori bazlı hızlı alt küme koşumu mekanizması hiç kurulmamış |

> Kanıtlar 2026-08-23 tarihinde doğrulandı.

### Kapsam dışı — bilinçli

| Ne | Neden |
|---|---|
| **Roslyn analyzer kuralları** (`AsyncLocal` yazımı, elle toplama ifadesi, `IAsyncEnumerable` gövdesinde `scope`) | Envanter kalem 3'ün asıl gövdesi. Üç kural + tanı metni + test + `.editorconfig` kaydı tek başına bir fazdır. Bu faz onu absorbe etmez; `denetim-paketi.py` bu sınıfları **aday** olarak yüzeye çıkararak sırasını hazırlar |
| **`[Trait]` kategorileri** | 2.897 test metoduna attribute eklemek yüksek hacim, düşük getiri. Proje bazında seçim (§91.1) kazancın çoğunu risksiz verir |
| **Skill metinlerinin kısaltılması, paralel kapanış** | **Faz 92.** Seçilen politika "önce kapı, sonra kısaltma" — 92 ancak 91 bittikten sonra neyin kapı kazandığını bilir |
| **1.315 manuel case'in otomatikleşmesi** | Envanter kalem 9, zincir işi |

---

## 91.0 — Taban çizgisini ölç (ilk iş, atlanamaz)

Repo kendi kuralını yazıyor: *"Ölçmediğin süreyi, yüzdeyi, oranı yazma."* Bu faz
"hızlandırdık" iddiası taşıyacaksa tabanı **önce** kaydetmelidir.

Ölçülecekler, `docs/hafiza/test-kosum-tuzaklari.md` içine yazılır (dosya bugün
8.063 B; bütçe dosya başına 16.000 B — yer var):

| Ölçüm | Nasıl |
|---|---|
| Soğuk ve sıcak `dotnet build` | frontend **açık** ve **kapalı**, ayrı ayrı |
| Tam `dotnet test` wall-clock | ve **proje başına** süre (19 test projesi) |
| `dotnet pack` · `dotnet format` | tek tek |
| `docs-site && npm run check` | dört alt kapının toplamı |
| **Kapanış kapısı toplamı** | yukarıdakilerin toplamı = bugünkü bedel |

🚨 Ölçüm `MSBUILDDISABLENODEREUSE=1` **ile** yapılır. O bayrak olmadan
`Templates.Tests` fixture'ı 8 dk+ asılı kalıyordu (`test-kosum-tuzaklari.md:15`);
bayraksız ölçüm tabanı yanlış şişirir.

---

## 91.1 — `scripts/kapi.py` · tek kapı koşucusu

Bugün bir faz boyunca en az **12 ayrı doğrulama komutu** elle koşuluyor: dört
.NET kapısı (denetim sonrası **tekrar**), sync kopya taraması, `secret`
taraması, `dokuman-bakim.py` (iki kez), `--site-denetle`, `npm run check`,
`build-agent-map.mjs --check`, filtreli test koşumu.

Üç aşama:

```bash
python3 scripts/kapi.py ic-dongu                 # build + etkilenen test projeleri
python3 scripts/kapi.py tarama                   # yalnız sync + secret (saniyeler)
python3 scripts/kapi.py kapanis --taban <sha>    # tamamı, ucuzdan pahalıya, tek özet
python3 scripts/kapi.py test --sinif "*Ad*"      # MTP filtresi, doğru biçimde
```

### Script'in devraldığı tek kaynaklar

| Bugün nerede | Yarın |
|---|---|
| `MSBUILDDISABLENODEREUSE=1` — `MEMORY.md` + `faz-uygulama` Adım 5 | script ortamı, her koşumda |
| Sync kopya `find` deseni — `faz-tamamlama:44` **ve** `ci.yml:42` | script; `ci.yml` script'i çağırır |
| `secret` regex'i — `faz-tamamlama:66` **ve** `ci.yml:65-66` | script; `ci.yml` script'i çağırır |
| Dört kapı komutu — `AGENTS.md` · `faz-tamamlama` · `kusur-giderme` (üç dosya) | script |
| MTP filtre biçimi — uyarı dört dosyada; **bayat komutun kendisi** `arsiv/fazlar/73:88`, `74:106`, `75:99`'da duruyor | `kapi.py test --sinif` |

### 🚨 İki tuzak yapısal olarak imkânsızlaşır

1. **`dotnet test --filter` sessizce yutulur.** `kapi.py test` derlenmiş ikiliyi
   `--filter-class` ile çağırır. Yanlış biçim yazılamaz çünkü ham komut
   agent'ın elinde değildir.
2. **`dotnet test ... | grep | head` boruyu kapatıp koşumu keser** — 16 projeden
   9'u koşup `exit 0` döndü (`test-kosum-tuzaklari.md:70-74`). Script çıktıyı
   **asla borulamaz**; özeti kendi üretir.

### Sözleşme

- Çıkış kodu 0 = tüm kapılar yeşil. Herhangi bir kırmızıda **sonraki kapı
  koşmaz** — ucuzdan pahalıya sıra korunur.
- Koştuğu her komutu ekrana basar. Ayıklama bilgisi kaybolmaz; `--komutlari-bas`
  hiçbirini koşmadan listeler.
- Süreler `artifacts/kapi-olcum.jsonl` içine eklenir (append). `artifacts/`
  zaten `UseArtifactsOutput` ağacıdır ve commit edilmez.
- **Etkilenen proje seçimi script içinde açık bir haritadır** (`src/X` →
  `tests/X.*` + `tests/Shared`), gizli sezgi değil. Harita eksikse `ic-dongu`
  o projeyi **atlamaz**, uyarı basar ve tam koşuma düşer — sessiz daralma
  yeşil bir yanlıştır.

### `AGENTS.md` ve CI

Kullanıcı kararı: **tek yol `kapi.py` olsun.** `AGENTS.md`'nin dört komutluk
bloğu tek satıra iner. Ham komutlar yalnız script'te yaşar; script onları
bastığı için ayıklama bilgisi kaybolmaz.

`ci.yml`'de sync ve `secret` adımları desenlerini **kopyalamayı bırakır**,
`kapi.py tarama` çağırır.

---

## 91.2 — Build döngüsünün incremental'liği

İki target'ın `Inputs`/`Outputs` çifti yok (`Frontend.targets:60` ve `:169`).
`AgentPrismBuildFrontend` (`:121`, Vitest + iki Vite build) **zaten** stamp ile
incremental'dir — **ona dokunulmaz**.

> 🚨 **Bu dosya ölçülmüş iki kusurun yorumunu taşır ve ikisi de korunmalıdır:**
>
> 1. `:145-155` — üç iç build (`net8.0`/`net9.0`/`net10.0`) paralel koşar ve tek
>    `wwwroot/` dizinine yazar; Vite dizini önce temizler. Ölçülen sonuç:
>    `ENOENT: ... unlink .../index-*.js`. Zincir bu yüzden **dış** build'de,
>    `DispatchToInnerBuilds` öncesinde koşar.
> 2. `:170-177` — zincir `DependsOnTargets` ile kurulur, target'lar arasında
>    değil. MSBuild `Condition`'ı `DependsOnTargets`'tan **önce** değerlendirir;
>    tersi yazılırsa `AgentPrismNpmInstall`'ın koşulu henüz boş olan
>    `AgentPrismNodeAvailable`'a bakar ve `AgentPrismDetectNode` hiç koşmaz.
>    Ölçülen sonuç: **UI sessizce derlenmiyordu.**

`faz-uygulama` Adım 1 gereği: **düzeltmeden önce üç TFM'li temiz build ile ölç,
düzelttikten sonra tekrar ölç.** İki kusurdan birini geri getiren bir düzeltme
kabul edilmez; ikisi de ayrı ayrı doğrulanır.

---

## 91.3 — `scripts/denetim-paketi.py` · denetim kanıt paketi

Denetçi bugün ham `git diff` okuyor. Büyük bir fazda bağlamın çoğu **toplamaya**
gidiyor, yargıya değil. Script bir taban sha'dan mekanik kalemleri önceden
çıkarır; denetçi bağlamını yargı gerektiren başlıklara ayırır.

| Çıktı | Beslediği denetim başlığı |
|---|---|
| `git diff --stat` + değişen dosya sınıfları | genel |
| `PublicAPI.Unshipped.txt` delta'sı | 3.6 plan dışı public API |
| Yeni/değişen test sınıfı ve metotları | 3.2 · 3.3 |
| **İddiası olmayan test metotları** (`Should`/`Assert` çağrısı yok) | 3.2 test tiyatrosu |
| **İmza-gövde kayması adayları**: diff'te eklenen üye × onu yazmayan `new <Tip>` ve nesne başlatıcı noktaları | 3.5 |
| Faz dokümanından çıkarılmış DoD satırları, **işaretsiz kutular ayrı** | 3.1 |
| `en.ts` ↔ `tr.ts` anahtar delta'sı | 3.8 |
| `.WithTags`/`.Produces` üstverisi olmayan yeni HTTP ucu | 3.8 |
| Muafiyet/taban çizgisi delta'sı (`DIAGRAM_EXEMPT`, `CLOSING_EXEMPT`, `SourceLanguageTests`) | 3.8 — cırcır kuralı 🔴 |
| `capabilities.md` `## Packages` satırı olmayan yeni `src/<Paket>/` | kalite sözleşmesi **E** — bugün hiçbir kapı yakalamıyor |

### Tarihsel tekrar oynatma — sha'lar (2026-08-23'te çözüldü)

Araç çalışıyorsa **geçmişte kaçırılmış** kusurları bugün göstermelidir. Üç vaka
ve aralıkları hazırdır; uygulayan oturum aramaz:

| Vaka | Taban | Hedef | Beklenen aday |
|---|---|---|---|
| Faz 20 — `CompleteAsync`'e `cost` eklendi, `Cost = cost` yazılmadı; **1068 test** kaçırdı | `7717ff1` | `9b05f4b` | `RunEventWriter.cs` imza-gövde kayması |
| Faz 68 (K-483) — `cached_input_cost` dört yüzeyde eksik; **4241 test** kaçırdı, bağımsız denetim buldu | `03f1ac3` | `2f5d4d0` | elle toplama / imza-gövde kayması |
| Faz 73 — "13 tanının 11'inde hiçbir şey doğrulanmıyordu" | `4580ed4` | `049ff30` | iddiası olmayan test |

Faz 20'nin hedef commit'i `src/AgentPrism.Core/Recording/RunEventWriter.cs`
dosyasına dokunur (doğrulandı) — aday oradan çıkmalıdır.

### Sınırları — bunlar sözleşmenin parçasıdır

- ⚠️ **Paket bir başlangıç noktasıdır, diff'in yerine geçmez.** Denetçi paketin
  işaretlediği her kalem için ham hunk'ı açar. Paket "temiz" derse denetçi yine
  de anlamsal başlıkları (3.1 DoD anlamı, 3.3 test seviyesi, 3.4 hata yolları)
  kendi okur.
- ⚠️ **İmza-gövde tespiti regex tabanlıdır ve yanlış pozitif üretir.** Çıktı
  "denetçi için aday" diye etiketlenir; **sert kapı değildir** ve çıkış kodunu
  kırmaz.

---

## 91.4 — `dokuman-bakim.py`'ye üç doküman sürüklenmesi kapısı

Üçü de bugün gerçekten var olan kusurlardan türetildi. Desen Faz 90'ın üç
kapısıdır (`tazelik_denetle`, `gecmis_isaretci_denetle`, `tam_metin_denetle`).

1. **`✅ Tamamlandı` fazında işaretsiz `- [ ]` kutusu.** Bugün 8 fazda 28 kutu;
   `71-WORKFLOW-KOD-DUGUMU.md`'de 14 — `- [ ] faz-denetim koşuldu` dahil, oysa
   dosya satır 149'da "🔴 yok" diyor. Kutu bayat, iş değil.
2. **Doküman iddiası ↔ repo gerçeği çakışması.** Somut vaka:
   `faz-denetim/SKILL.md:114` "`EnablePublicApiTracking` bugün `false`" diyor;
   `Directory.Build.props:58` `true` diyor. Kapı, doküman ve skill metnindeki
   MSBuild özellik iddialarını gerçek `.props` değerine karşı denetler.
3. **Tekrarlanan komut ve regex.** §91.1'den sonra dört kapı komutu, `secret`
   regex'i ve sync `find` deseni tek yerde yaşamalıdır. Kapı ikinci kopyayı arar.

**Ek iş — sayaç sürüklenmesi.** Aynı kusur sınıfının sayacı üç dosyada üç farklı
değer taşıyor. Sayaçlar tek kaynağa (`docs/hafiza/<alan>.md`) iner; `AGENTS.md`
ve skill'ler sayı yazmaz, oraya bağlanır. Kapı, kayıtlı bir sınıf adının yanında
başka dosyada çıplak sayı arar.

---

## 91.5 — Yeniden ölç ve kaydet

§91.0'ın ölçümleri tekrarlanır. Öncesi/sonrası bu dokümana **sayıyla** yazılır.
Ölçüm iyileşme göstermiyorsa bu bir başarısızlık değil, bir **bulgudur** ve
aynen yazılır — gizlenmez.

---

## Planlanan Public API

**Büyümüyor.** Bu faz `src/` altında yalnız bir `.targets` dosyasına dokunur;
hiçbir C# public üyesi eklenmez, değişmez veya kaldırılmaz.

`PublicAPI.Shipped.txt` dosyaları bugün boştur (16 dosya × 1 satır),
`Unshipped.txt` toplamı 8079 satırdır. Faz 7'den önce yüzey büyütmek hâlâ
ucuzdur — bu faz o bütçeden **hiç harcamaz**.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok. `src/AgentPrism.UI/frontend/` kaynaklarına dokunulmaz; yalnız o kaynakları
derleyen MSBuild target'ının incremental'liği düzeltilir. Bundle boyutu
değişmemelidir — DoD bunu ölçer.

---

## Planlanan Dosya Listesi

```
scripts/
├── kapi.py                      (yeni)
├── kapi_test.py                 (yeni — CI deseni: -p "*_test.py")
├── denetim-paketi.py            (yeni)
├── denetim_paketi_test.py       (yeni)
├── dokuman-bakim.py             (üç yeni denetim)
└── dokuman_bakim_test.py        (üç yeni denetimin testleri)

.github/workflows/ci.yml         (sync + secret adımları kapi.py'yi çağırır)
src/AgentPrism.UI/AgentPrism.UI.Frontend.targets   (iki target incremental)
AGENTS.md                        (dört kapı bloğu → tek satır)
.agents/skills/faz-denetim/SKILL.md      (bayat EnablePublicApiTracking iddiası)
docs/hafiza/test-kosum-tuzaklari.md      (§91.0 ve §91.5 ölçümleri)
docs/manuel-test/33-DOKUMAN-KAPILARI.md  (§91.4 case'leri)
docs/manuel-test/36-GELISTIRME-KAPILARI.md (yeni aile + 00-INDEKS satırı)
```

🚨 **Test dosyası adı alt çizgi taşır.** CI `python3 -m unittest discover -s
scripts -p "*_test.py"` koşar (`ci.yml:135`). Tire taşıyan dosya sessizce
atlanır — Faz 90'da yaşandı.

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetildi. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| `kapi.py` bir kapıyı sessizce atlar ve yeşil döner | Birim (`kapi_test.py`) | Sahte koşucu ile her aşamanın komut listesi doğrulanır |
| `kapi.py` kırmızı kapıdan sonra pahalı kapıyı yine koşar | Birim | İlk kırmızıda sonraki komutun **çağrılmadığı** doğrulanır |
| `kapi.py test` yanlış MTP biçimi üretir (`--filter`) | Birim | Üretilen komut satırında `--filter-class` aranır, `--filter ` **yasaklanır** |
| Etkilenen proje haritası eksik kalır, koşum sessizce daralır | Birim | Haritada olmayan `src/` projesi → uyarı **ve** tam koşuma düşüş |
| `secret`/sync deseni script ile CI arasında ayrışır | Birim | `ci.yml` deseni **içermemeli**; kapı ikinci kopyayı arar |
| `denetim-paketi.py` imza-gövde kaymasını kaçırır | Birim + **tarihsel tekrar oynatma** | Faz 20 ve Faz 68 aralıklarında aday üretmeli |
| `denetim-paketi.py` iddiasız testi kaçırır | Birim + tarihsel | Faz 73 aralığında aday üretmeli |
| `denetim-paketi.py` yanlış pozitifi 🔴 gibi sunar | Birim | Çıktı "aday" etiketli; çıkış kodu **kırılmaz** |
| Yeni doküman kapısı var olan temiz repoda ötmeye başlar | Birim (`dokuman_bakim_test.py`) | Düzeltmeden **önce** 28 kutuyu bulmalı, **sonra** sıfır dönmeli |
| `Frontend.targets` düzeltmesi `wwwroot` yarışını geri getirir | **Fonksiyonel** — üç TFM'li temiz build | `dotnet build` üç hedefte; `ENOENT` yok, `wwwroot` tam |
| `Frontend.targets` düzeltmesi UI'ı sessizce derlemez | **Fonksiyonel** + örnek uygulama | `AgentPrismValidateFrontendAssets` (pack) yeşil **ve** örnek uygulamada arayüz açılır |
| Incremental düzeltme stamp'i bozar, UI hiç yeniden derlenmez | Fonksiyonel | Frontend kaynağı değiştir → build → `wwwroot` yenilendi mi |

Bu fazda **kiracı, akış, depo ve HTTP sınırı geçen davranış yoktur**; beş
sorudan (iptal · eşzamanlılık · boş/aşırı girdi · başka kiracı · alt sistem
hatası) uygulanabilir olanlar script seviyesinde karşılanır: boş/bozuk girdi
(taban sha yok, git yok, faz dokümanı yok) ve alt sistem hatası (`dotnet`
bulunamadı, `npm` yok) her iki script'te test edilir.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Temiz çalışma ağacı | `python3 scripts/kapi.py tarama` | Çıkış kodu 0; "temiz" özeti |
| 2 | `tests/` altına elle `Ornek 2.cs` kopyası koy | `python3 scripts/kapi.py tarama` | Çıkış kodu 1; dosya **adıyla** raporlanır. 🚨 `git add` edildiyse de bulunmalı |
| 3 | Boş `docs/` dizini kopyası yok; normal repo | `python3 scripts/kapi.py --komutlari-bas` | Hiçbir komut **koşmaz**; dört kapı komutu listelenir |
| 4 | Faz 20 sha aralığı | `python3 scripts/denetim-paketi.py --taban 7717ff1 --hedef 9b05f4b` | `Cost` alanı imza-gövde adayı olarak görünür |
| 5 | `71-WORKFLOW-KOD-DUGUMU.md` düzeltilmeden | `python3 scripts/dokuman-bakim.py --denetle` | 14 işaretsiz kutu raporlanır |
| 6 | Frontend kaynağı değişmemiş | Art arda iki `dotnet build` | İkincisinde `npm run build` **koşmaz**; süre farkı ölçülüp yazılır |
| 7 | 👤 insan gerekir — örnek uygulama ayakta | Tarayıcıda arayüzü aç | Arayüz yüklenir; `Frontend.targets` düzeltmesi gömülü varlıkları bozmamış |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `kapi.py ic-dongu` frontend'i varsayılan olarak kapatsın mı? | A: `src/AgentPrism.UI/` veya `frontend/` değişmediyse otomatik kapat · B: her zaman açık | **A** — `faz-uygulama` Adım 5 zaten elle bunu söylüyor; otomatikleştirmek tuzağı (E2E koşarken kapatmak) da script'e taşır. Uygulama anında ölçülüp karara bağlanır |
| 2 | Sayaç tek kaynağı hangi dosya olsun? | A: `docs/hafiza/<alan>.md` içinde kalsın, diğerleri bağlansın · B: yeni `docs/hafiza/kusur-siniflari.md` | **A** — yeni dosya bütçe ve indeks satırı ister; kalem 20'nin şikâyetini büyütür |
| 3 | `kapi.py kapanis` site kapılarını da koşsun mu? | A: koşsun (`npm run check` dahil) · B: ayrı aşama | **A** — kapanışta zaten koşuluyor; ayrı tutmak tekrar üretir. `--site-atla` kaçış kapısı bırakılır |

---

## Bitiş Ölçütleri (DoD)

- [ ] §91.0 taban ölçümleri `docs/hafiza/test-kosum-tuzaklari.md` içine yazıldı: soğuk/sıcak build (frontend açık ve kapalı), tam `dotnet test`, proje başına süre, `pack`, `format`, `npm run check`, toplam
- [ ] `python3 scripts/kapi.py tarama` temiz repoda 0, elle konmuş `* 2.*` kopyasında 1 döner
- [ ] `python3 scripts/kapi.py --komutlari-bas` dört kapı komutunu hiçbirini koşmadan listeler
- [ ] `kapi.py test --sinif` derlenmiş ikiliyi `--filter-class` ile çağırır; üretilen komut satırında `--filter ` **yok**
- [ ] `ci.yml` sync `find` desenini ve `secret` regex'ini **içermez**; `kapi.py tarama` çağırır
- [ ] `AGENTS.md` dört komutluk blok yerine tek `kapi.py` satırı taşır
- [ ] `Frontend.targets` düzeltmesi öncesi/sonrası üç TFM'li temiz build süresi ölçüldü ve yazıldı; `wwwroot` yarışı **ve** UI'ın gerçekten derlendiği ayrı ayrı doğrulandı
- [ ] Art arda iki `dotnet build`'de ikincisi `npm run build` koşmaz — ölçüldü
- [ ] `denetim-paketi.py` üç tarihsel vakayı gösterir: Faz 20 (`Cost = cost`, 1068 test kaçırdı) · Faz 68 (K-483, 4241 test kaçırdı) · Faz 73 (13 tanının 11'i iddiasız). Üçünden biri çıkmıyorsa DoD karşılanmaz
- [ ] `denetim-paketi.py` çıktısı yanlış pozitifleri "aday" etiketiyle sunar ve çıkış kodunu kırmaz
- [ ] `dokuman-bakim.py --denetle` 28 işaretsiz kutuyu ve bayat `EnablePublicApiTracking` iddiasını raporlar; ikisi de düzeltilir ve kapı sonra sıfır döner
- [ ] Kusur sınıfı sayaçları tek kaynağa indi; üç dosyadaki 3/4/5 çelişkisi kapandı
- [ ] `python3 -m unittest discover -s scripts -p "*_test.py"` yeşil
- [ ] **Ölçüm karşılaştırması yazıldı**: §91.0 ve §91.5 rakamları yan yana. İyileşme yoksa bulgu olarak yazıldı
- [ ] **Eklenen script satırı ile düşen prose satırı ölçüldü ve yazıldı.** Script daha büyükse gerekçesi yazılır — bu bir kapı değil, envanter kalem 20'ye karşı bir hesap verme satırıdır
- [ ] Dört doğrulama kapısı sıfır uyarı verir (`kapi.py kapanis` ile)
- [ ] `samples/AgentPrism.Api` ayağa kalktı ve arayüz tarayıcıda açıldı — `Frontend.targets` düzeltmesinin gerçek kanıtı budur; çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri eklendi: §91.4 kapıları `33-DOKUMAN-KAPILARI.md`'ye, `kapi.py`/`denetim-paketi.py` case'leri yeni `36-GELISTIRME-KAPILARI.md`'ye + `00-INDEKS.md` satırı. Otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] Faz 92 dokümanı (skill konsolidasyonu + paralel kapanış) devir teslim kalitesinde yazıldı; bu fazda **hangi tuzağın kapı kazandığı** listelendi — 92 kısaltmayı o listeye göre yapar

### Doğrulama komutları

```bash
# Kapı koşucusu
python3 scripts/kapi.py tarama
python3 scripts/kapi.py --komutlari-bas
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>

# Tarihsel tekrar oynatma — bu fazın en güçlü kanıtı
python3 scripts/denetim-paketi.py --taban 7717ff1 --hedef 9b05f4b   # Faz 20
python3 scripts/denetim-paketi.py --taban 03f1ac3 --hedef 2f5d4d0   # Faz 68
python3 scripts/denetim-paketi.py --taban 4580ed4 --hedef 049ff30   # Faz 73

# Doküman kapıları
python3 scripts/dokuman-bakim.py --denetle
python3 -m unittest discover -s scripts -p "*_test.py"

# Build incremental'liği
dotnet build AgentPrism.slnx -c Release          # birinci
dotnet build AgentPrism.slnx -c Release          # ikinci: npm run build koşmamalı
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| `Frontend.targets` düzeltmesi ölçülmüş iki kusuru geri getirir (`wwwroot` yarışı · UI sessizce derlenmiyor) | Üç TFM'li temiz build ile önce/sonra ölç; iki kusuru **ayrı ayrı** doğrula. Şüphe varsa düzeltmeyi geri al ve gerekçeyi yaz — bu target'lar ucuz değil |
| `kapi.py` yeni bir bakım aparatı olur — envanter kalem 20'nin tam şikâyeti | DoD'de hesap verme satırı var: eklenen script satırı ile düşen prose satırı ölçülür ve yazılır. Script **eklemez, yerine geçer**; `ci.yml` ve `AGENTS.md` desenleri kopyalamayı bırakır |
| İmza-gövde regex'i gürültü üretir, denetçi paketi ciddiye almaz | "Aday" etiketi zorunlu; çıkış kodu kırılmaz. Tarihsel tekrar oynatma yanlış pozitif oranını da ölçer ve yazar |
| Denetçi paketi diff'in yerine koyar ve anlamsal bulguyu kaçırır | Paket sözleşmesi bunu açıkça yasaklar; `faz-denetim` çağrı metni "paket başlangıç noktasıdır" cümlesini taşır |
| Yeni doküman kapısı geçmiş fazları toplu kırmızıya çevirir ve kapı gevşetilir | 28 kutu bu fazda **düzeltilir**, muaf tutulmaz. Kızaran kapı düzenlemenin kusurudur, kapının değil |
| `AGENTS.md`'den ham komutların düşmesi ayıklamayı zorlaştırır | `kapi.py` koştuğu her komutu basar; `--komutlari-bas` hiçbirini koşmadan listeler |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz. **Bu fazda hangi tuzağın kapı kazandığı** burada
> listelenir — Faz 92 kısaltmayı o listeye göre yapar.
