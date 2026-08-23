# AgentPrism — AGENTS.md

> **Merkezi agent talimat dosyası** — her AI kod agent'ı için tek kaynak budur.
> `CLAUDE.md` buna symlink'tir; platform-spesifik ayrı talimat dosyası oluşturma.

---

## Okuma Protokolü — Önce Bunu Uygula

Bu repo büyüktür ve dokümanların çoğu birikimli defterdir. **Hiçbir dokümanı
ihtiyacın olmadan baştan sona okuma.**

**Oturum başında yalnız:** bu dosya · [`MEMORY.md`](MEMORY.md) · çalıştığın fazın
dokümanı (`docs/NN-*.md`; kapanmış fazlar `docs/arsiv/fazlar/`) ve onun "Bu Faza
Başlarken" listesi. **Sonra, yalnız dokunduğun alan için** — her dosyanın tek bir
işi vardır, aradığın iş bu tablodadır:

| İhtiyaç | Yol |
|---|---|
| Bir tuzak/desen var mı? | [hafiza/00-INDEKS](docs/hafiza/00-INDEKS.md) → alan dosyası |
| Bir şey nerede yaşıyor? | [hafiza/kod-haritasi](docs/hafiza/kod-haritasi.md) |
| Bir karar alınmış mı? | [KARARLAR-INDEKS](docs/KARARLAR-INDEKS.md) → `grep -n "K-059" docs/KARARLAR.md` |
| Mimari resim (katman, veri modeli, çalıştırma yolu) | [MIMARI](docs/MIMARI.md) — ilgili bölüm |
| Güvenlik modeli (kiracı, rol, denetim izi, sandbox) | [MIMARI-GUVENLIK](docs/MIMARI-GUVENLIK.md) |
| MAF genişleme noktası | [MAF-GENISLEME-NOKTALARI](docs/MAF-GENISLEME-NOKTALARI.md) |
| Faz durumu (**üretilen**) · adaylar · keşif turları | [YOL-HARITASI](docs/YOL-HARITASI.md) · [ADAYLAR](docs/ADAYLAR.md) · [kesif/](docs/kesif/) |
| Elle koşulan kabul testi | [manuel-test/00-INDEKS](docs/manuel-test/00-INDEKS.md) — koşumu `manuel-test-kosumu` yürütür |
| Geçmişte neden öyle yapıldı? | [arsiv/](docs/arsiv/) — kapanmış kayıt, yalnız grep'le |
| Paketler ve kurulum | [README](README.md) |
| Kullanıcıya dönük ürün metni | [docs-site/](docs-site/) — **İngilizce**, `docs/` ile karıştırma |

**`KARARLAR.md` ve `arsiv/*` baştan sona okunmaz.** İndeksten satır numarasını al,
`sed -n 'N,Np'` ile oku. Aramak okumaktan ucuzdur: `grep -rn "AsyncLocal" docs/`.

---

## Temel İletişim Kuralları

**Kullanıcının dilinde yanıt ver.** Türkçe soruya Türkçe, diğer her dile
İngilizce. Kod ve commit mesajı hep İngilizce.

**Tüm yanıtlar ASD-STE100 Simplified Technical English kurallarına uyar** — kısa
cümle, tek fikir, aktif çatı. Türkçe yanıtta da geçerli; ç/ğ/ı/ö/ş/ü kullan.

**Teknik terimi çevirme.** Terim İngilizce kalır, Türkçe ek alır (`secret`,
`store`, `endpoint`, `scope`, `span`, `run`, `tool`): doğru "`secret` yazılmaz",
yanlış "sır yazılmaz".

**Her belirsizliği sor.** Edge-case veya tasarım kararı çıktığında varsayım yapma;
durumu tarif ederek kullanıcıya sor. Plan modundaysan en küçüğünü bile sor.

**Uzun vadeli mimari kararlar al** — sonra değiştirilmek üzere tasarlanmış geçici
çözüm önerme.

**Karar defteri:** Daha önce reddedilmiş işleri yeniden önerme — önce
[`docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md).

---

## Bu Proje Nedir

AgentPrism, Microsoft Agent Framework (MAF) üzerine kurulu bir **NuGet paket
ailesidir** — bir uygulama değil, başkalarının bağımlı olacağı bir kütüphane.
Kalite eşiğini bu belirler:

- Public API'de kırıcı değişiklik pahalıdır — tasarımı ilk seferde doğru yap
- Her public üye XML dokümanına sahip olmalıdır (build bunu zorlar)
- Tüketicinin bağımlılık grafiğini kirletme
- `TryAdd*` ile kaydet; tüketicinin kaydı her zaman kazanmalı

---

## Faz Akışı ve Doküman Disiplini

**Geliştirme fazlar hâlinde, çoğu zaman ayrı sohbetlerde yapılır.** Sonraki
oturum repo'yu sıfırdan okur ve yalnız dokümanlara güvenir; bu yüzden her
geliştirme sonrası dokümanlar gözden geçirilir.

- Doküman ile kod çelişirse **doküman yanlıştır** — koda göre düzeltilir
- Plandan sapma **gizlenmez**, gerekçesiyle yazılır — en değerli bilgi odur
- Sonraki fazın dokümanı **devir teslim kalitesine** çıkarılır; ayrı bir sohbet
  onunla tek başına çalışabilmeli
- Karar defterine yalnız public API/compatibility contract, güvenlik veya kiracı
  sınırı, kalıcı veri/migration ya da geri dönüşü pahalı sistem kararı girer.
  Yerel implementation tercihi faz dokümanında veya kod yorumunda kalır; yeni
  `K-*` kaydı açılmaz.
- Keşfedilen tuzak **alan dosyasına** (`docs/hafiza/`) yazılır, `MEMORY.md`'ye değil
- Birikimli anlatı `docs/arsiv/`'e gider — sıcak yol büyümezse her oturum ucuz başlar

### Doküman bütçesi (zorunlu)

Başlangıç bağlamı yalnız bu dosya, `MEMORY.md` ve aktif fazdır. Sorgu bağlamı
(indeksler, mimari, alan hafızası) gerektiğinde okunur; karar/aday kayıtları
ledger'dır, açılışta okunmaz. `scripts/dokuman-bakim.py` bu üç katmanı ayrı
raporlar. Bütçe aşılırsa **içerik silinmez** — taşınır.

**Arşivlemek taşımak değil, damıtmaktır.** Faz kapanışında `faz-arsivle` +
`faz-damit` koşar: plan düşer, kalıcı bilgi kalır, tam metin git'te durur ve
her denetimde çözülebilirliği kanıtlanır. Muaf ağaçlar dahil **her ağacın kendi
bütçesi vardır**. Kural: [`docs/arsiv/fazlar/INDEKS.md`](docs/arsiv/fazlar/INDEKS.md).

### Faz durumu

Tek kaynak [`docs/YOL-HARITASI.md`](docs/YOL-HARITASI.md)'dir — **üretilir**,
elle yazılmaz (K-413). Seçilmemiş kalemler [`docs/ADAYLAR.md`](docs/ADAYLAR.md)
içindedir. Bu listeyi başka dosyada tekrarlama — iki yerde tutmak kayma üretir.

Faz bittiğinde **`faz-denetim` ve `faz-tamamlama` uygulanır.** Atlanmaz; kapanış
kodu, `docs/manuel-test/` kabul case'lerini ve `docs-site/`'ı birlikte kapsar.
`✅ Tamamlandı` durumundaki faz dokümanı kökte kalamaz; denetim bunu hata sayar.

---

## Dal ve Commit

**Ana dalda çalışılabilir.** Repo tek bakımcılıdır; faz dalı bir tur
maliyeti ekliyordu. Genel "yalnız feature dalı" konvansiyonu burada
geçersizdir. Geri alması pahalı veya deneysel iş için yine de dal aç.
Commit'i **kullanıcı istemedikçe atma**.

## Doğrulama Kapıları

Dördü de sıfır uyarı vermelidir. Bir tanesi kırmızıysa iş **bitmemiştir**.

```bash
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

Ölçüldü: sıcak build ~5 sn — **erken ve sık çalıştır**. `dotnet format`,
`dotnet build`'in yakalamadığı analyzer tanılarını yakalar; dördü de bu yüzden
zorunludur.

**Hızlı iç döngü.** Arayüze dokunmuyorsan `-p:AgentPrismFrontendEnabled=false`
npm/Vite/Vitest adımlarını atlar. Dördünün tamamı **faz kapanışında** ve
arayüz/paket değişiminde çalışır. E2E tuzağı: `faz-uygulama` Adım 5.

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır. Bir analyzer
kuralını bastırmadan önce **neden** tetiklendiğini anla; bastırma gerekiyorsa
gerekçesini koda ve `docs/KARARLAR.md`'ye yaz.

**`secret` asla dosyaya yazılmaz.** Bağlantı dizesi ve API anahtarı yalnız
`dotnet user-secrets` içinde yaşar; `appsettings.json` boş placeholder taşır.
Faz sonunda `secret` taraması yapılır (`faz-tamamlama`).

`dotnet build` **arayüzü de derler**: `npm ci` → `tsc --noEmit` → Vitest → Vite →
Brotli → bundle bütçesi (250 KB gzip). Node.js 20.19+ gerekir.

---

## MCP Yetenekleri

`.mcp.json` iki server tanımlar (onay sonrası aktif): `playwright` arayüz
değişikliğini tarayıcıda görsel doğrular; `context7` MAF-dışı bağımlılık
dokümanı getirir — MAF tipi için `maf-api-kesfi` kullan.

---

## Skill'ler (Ortak İş Akışları)

Tekrarlanan iş akışları `.agents/skills/<ad>/SKILL.md` altındadır — talimat
burada tekrarlanmaz, skill okunup uygulanır. Zincir sırayla: `aday-kesfi`
(yalnız istek üzerine) → `faz-planlama` (aday F-NN faza
dönüşürken) → `faz-baslangic` (okuma protokolü) →
`faz-uygulama` (**ilk kod satırından önce**) → `faz-denetim` (bağımsız
denetçi; 🔴 bulgu kapanmadan faz bitmez) → `faz-tamamlama` (kapanış;
tüketici yüzeyine dokunulduysa `tuketici-dokuman-senkronu`'nu çağırır).
Zincir dışı: `maf-api-kesfi` (MAF tipini ilk kez kullanmadan önce) ·
`kusur-giderme` (kusur bulunduğunda) · `manuel-test-kosumu` (kabul setinin
tamamında).
Konvansiyon: [`.agents/skills/README.md`](.agents/skills/README.md).

---

## Kodlama Kuralları (Bu Repo'ya Özgü)

Genel .NET kuralları `.editorconfig`'dedir. Aşağıdakiler analyzer'ın
yakalayamadığı, projeye özgü kurallardır.

**🚨 `docs/` ile `docs-site/` iki ayrı şeydir.** `docs/` Türkçe geliştirme
günlüğü, `docs-site/` İngilizce **ürün dokümantasyonudur**. Aynı içeriği iki yere
yazma: kullanıcıya dönük anlatı siteye, geliştirme kaydı `docs/`'a. **Site fazın
kapanışına dahildir** (`faz-tamamlama` Adım 7). Yayın hattı, üretilen sayfalar ve
ekran görüntüleri: [`docs/hafiza/dokumantasyon.md`](docs/hafiza/dokumantasyon.md).

**🚨 Dil sınırı — pakete giren veya çalışma anında çalışan her şey İngilizce'dir.**
Geliştirme aparatı (`docs/`, `.agents/skills/`, `scripts/`) Türkçe kalır;
`locales/tr.ts` meşru sözlüktür (K-228). Kapı: `SourceLanguageTests` — taban
çizgisi **yalnız küçülür**. Kapsam listesi:
[`docs/hafiza/dokumantasyon.md`](docs/hafiza/dokumantasyon.md).

**MAF tiplerini sarmalama.** `AIAgent`, `AgentSession`, `ChatMessage`,
`AIFunction` doğrudan kullanılır. AgentPrism bir kontrol düzlemidir, bir
soyutlama katmanı değil.

**`Activity.Current` ve `AsyncLocal` async yardımcı metotta açılmaz.** Yazım
çağırana geri akmaz; `span`/`scope` çağıran metodun **kendi gövdesinde** başlatılır
ve akışlı yolda her `MoveNextAsync` öncesi tekrarlanır. Beş kez yaşandı —
`docs/hafiza/cekirdek-calistirma.md`.

**Tool'lar yalnızca kodda tanımlanır.** Arayüzden agent oluşturulabilir; tool
**kodu** yazılamaz. Bu bir güvenlik sınırıdır ve gevşetilmez.

**AOT uyumluluğu.** Listeyi `grep -l "AotCompatible>false" src/*/*.csproj` ile
doğrula. `reflection` kullanma; kaçış merdiveni:
[`docs/hafiza/build-ve-analyzer.md`](docs/hafiza/build-ve-analyzer.md).

**Ön sürüm MAF paketleri yalnızca `AgentPrism.AspNetCore` içinde** (K-008).

**`secret` veritabanına da yazılmaz** (K-059). Kayıtta yalnızca değerin
okunacağı **yapılandırma anahtarının adı** durur; değer çalışma anında
`IConfiguration` üzerinden çözülür.

**Gözlemlenebilirlik işlevselliği bozmaz.** `run` kaydı `store`'u hata verirse
`run` devam eder, hata loglanır.

**`ValueTask` dönen arayüzlerde `ConfigureAwait(false)`.** Kütüphane kodudur.

**Arayüz metni sözlükten gelir — iki dilde.** `locales/en.ts` anahtar kümesinin
kaynağıdır, `tr.ts` onu `Messages` tipiyle karşılar: eksik anahtar **derleme
hatasıdır** (K-228). Sunucu yanıtları çevrilmez (K-232). `docs/hafiza/frontend.md`.

**İmza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır.** Yeni bir
alan/parametre eklerken çağrı zincirindeki her katmanın **gövdesini** elle izle.
Faz 20'de 1068 test bunu kaçırdı. Kontrol listesi: `faz-uygulama` Adım 4.

**Bir davranış sınır geçiyorsa birim testi onu kanıtlamaz.** Sınır: DI · HTTP ·
kiracı · akış · depo · paket. Seviye tablosu: `faz-uygulama` Adım 2.

---

## Diyagram Kuralı

**Her diyagram Mermaid ile yazılır.** ASCII kutu çizimi (`┌─┐│└┘`) kullanılmaz —
elle hizalanır, bayatlar ve `git diff`'i bozar. **İstisna:** dizin ağaçları düz
metin kod bloğu kalır. Koddan sapmışsa **diyagram yanlıştır**. Tip seçimi ve
yazım tuzakları: [`docs/hafiza/dokumantasyon.md`](docs/hafiza/dokumantasyon.md).
