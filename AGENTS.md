# Tracon — AGENTS.md

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
| Bir şey ters gitti — hangi rampa? | [ortak/kurtarma](.agents/ortak/kurtarma.md) — `KR-01…12` |
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

Tracon, Microsoft Agent Framework (MAF) üzerine kurulu bir **NuGet paket
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
fazdan sonra dokümanlar gözden geçirilir.

- Doküman ile kod çelişirse **doküman yanlıştır** — koda göre düzeltilir
- Plandan sapma **gizlenmez**, gerekçesiyle yazılır — en değerli bilgi odur
- Sonraki fazın dokümanı **devir teslim kalitesine** çıkarılır; ayrı bir sohbet
  onunla tek başına çalışabilmeli
- Karar defterine yalnız public API/compatibility, güvenlik veya kiracı sınırı,
  kalıcı veri/migration ya da geri dönüşü pahalı sistem kararı girer. Yerel
  tercih faz dokümanında veya kod yorumunda kalır; `K-*` açılmaz.
- Keşfedilen tuzak **alan dosyasına** (`docs/hafiza/`) yazılır, `MEMORY.md`'ye değil
- Birikimli anlatı `docs/arsiv/`'e gider — sıcak yol büyümezse her oturum ucuz başlar

### Doküman bütçesi (zorunlu)

Başlangıç bağlamı yalnız bu dosya, `MEMORY.md` ve aktif fazdır. Sorgu bağlamı
(indeks, mimari, alan hafızası) gerektiğinde okunur; karar/aday kayıtları
ledger'dır ve açılışta okunmaz. `scripts/dokuman-bakim.py` üç katmanı ayrı
raporlar. Bütçe aşılırsa **içerik silinmez** — taşınır.

**Arşivlemek taşımak değil, damıtmaktır.** Faz kapanışında `faz-arsivle` +
`faz-damit` koşar: plan düşer, kalıcı bilgi kalır, tam metin git'tedir ve her
denetimde çözülebilirliği kanıtlanır. Muaf ağaçlar dahil **her ağacın kendi
bütçesi vardır**. Kural: [`docs/arsiv/fazlar/INDEKS.md`](docs/arsiv/fazlar/INDEKS.md).

### Faz durumu

Tek kaynak [`docs/YOL-HARITASI.md`](docs/YOL-HARITASI.md)'dir — **üretilir**,
elle yazılmaz (K-413). Seçilmemiş kalemler [`docs/ADAYLAR.md`](docs/ADAYLAR.md)
içindedir. Bu listeyi başka dosyada tekrarlama — iki yerde tutmak kayma üretir.

Faz bittiğinde **`faz-denetim` ve `faz-tamamlama` uygulanır.** Atlanmaz; kapanış
kodu, `docs/manuel-test/` kabul case'lerini ve `docs-site/`'ı birlikte kapsar.
`✅ Tamamlandı` faz dokümanı kökte kalamaz; denetim bunu hata sayar.

---

## Dal ve Commit

**Ana dalda çalışılabilir.** Repo tek bakımcılıdır; faz dalı bir tur
maliyeti ekliyordu. Genel "yalnız feature dalı" konvansiyonu burada
geçersizdir. Geri alması pahalı veya deneysel iş için yine de dal aç.
Commit'i **kullanıcı istemedikçe atma**.

## Doğrulama Kapıları

Dördü de sıfır uyarı vermelidir. Bir tanesi kırmızıysa iş **bitmemiştir**.

```bash
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>
```

Tam anlatı — komut yüzeyi, neden dördü de zorunlu, hızlı iç döngü, `secret` ve
ortam kuralları — [`.agents/ortak/kapilar.md`](.agents/ortak/kapilar.md)
içindedir (Faz 91 · 92).

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
denetçi; 🔴 bulgu kapanmadan faz bitmez) → `faz-tamamlama` (kapanış; kod
donduktan sonra **kulvarlı** koşar — şema skill'in kendisindedir). Zincir dışı:
`maf-api-kesfi` (MAF tipini ilk kez kullanmadan) · `kusur-giderme` ·
`manuel-test-kosumu` (kabul setinin tamamında) · `nuget-danismani` (yayın
kararı; zincirin **üstünde**, tek fazı değil yayınlanacak ürünü yargılar).
Ortak sözleşme (kapı, test seviyeleri): `.agents/ortak/`. Konvansiyon:
[`.agents/skills/README.md`](.agents/skills/README.md).

---

## Kodlama Kuralları (Bu Repo'ya Özgü)

Genel .NET kuralları `.editorconfig`'dedir. Aşağıdakiler analyzer'ın
yakalayamadığı kurallardır; gerekçe ve vaka okun gösterdiği alan dosyasındadır.
Tek alana özgü olanlar (AOT muafiyeti, arayüz sözlüğü) orada yaşar.

**🚨 `docs/` ile `docs-site/` iki ayrı şeydir** — geliştirme kaydı `docs/`'a,
kullanıcıya dönük anlatı siteye. **Site fazın kapanışına dahildir**
(`faz-tamamlama` Adım 7). **🚨 Dil sınırı:** pakete giren ve çalışma anında
çalışan her şey İngilizce'dir; geliştirme aparatı (`docs/`, `.agents/`,
`scripts/`) Türkçe kalır ve `locales/tr.ts` meşru sözlüktür (K-228). Kapı:
`SourceLanguageTests`, tabanı **yalnız küçülür**.
→ [`hafiza/dokumantasyon.md`](docs/hafiza/dokumantasyon.md)

**MAF tiplerini sarmalama.** `AIAgent`, `AgentSession`, `ChatMessage`,
`AIFunction` doğrudan kullanılır — Tracon kontrol düzlemidir, soyutlama değil.

**`Activity.Current` ve `AsyncLocal` async yardımcı metotta açılmaz** — yazım
çağırana geri akmaz. `span`/`scope` çağıranın **kendi gövdesinde** açılır.
→ [`hafiza/cekirdek-calistirma.md`](docs/hafiza/cekirdek-calistirma.md)

**Tool'lar yalnızca kodda tanımlanır.** Arayüzden agent oluşturulabilir; tool
**kodu** yazılamaz. Bu bir güvenlik sınırıdır ve gevşetilmez.

**Ön sürüm MAF paketi yalnızca `Tracon.AspNetCore` içinde** (K-008).

**`secret` veritabanına da yazılmaz** (K-059) — kayıtta yalnızca **anahtarın
adı** durur. → [`MIMARI-GUVENLIK.md`](docs/MIMARI-GUVENLIK.md)

**Gözlemlenebilirlik işlevselliği bozmaz.** `run` kaydı `store`'u hata verirse
`run` devam eder, hata loglanır.

**`ValueTask` dönen arayüzlerde `ConfigureAwait(false)`.** Kütüphane kodudur.

**İmza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır** — yeni alan/parametre
eklerken her katmanın **gövdesini** elle izle. Liste: `faz-uygulama` Adım 4.

**Bir davranış sınır geçiyorsa birim testi onu kanıtlamaz.** Sınır: DI · HTTP ·
kiracı · akış · depo · paket. → [`ortak/test-seviyeleri.md`](.agents/ortak/test-seviyeleri.md)

---

## Diyagram Kuralı

**Her diyagram Mermaid ile yazılır.** ASCII kutu çizimi (`┌─┐│└┘`) kullanılmaz —
elle hizalanır, bayatlar ve `git diff`'i bozar. **İstisna:** dizin ağaçları düz
metin kod bloğu kalır. Koddan sapmışsa **diyagram yanlıştır**. Tip seçimi ve
yazım tuzakları: [`docs/hafiza/dokumantasyon.md`](docs/hafiza/dokumantasyon.md).
