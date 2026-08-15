# AgentPrism — AGENTS.md

> **Merkezi agent talimat dosyası** — her AI kod agent'ı için tek kaynak budur.
> `CLAUDE.md` buna symlink'tir; platform-spesifik ayrı talimat dosyası oluşturma.

---

## Okuma Protokolü — Önce Bunu Uygula

Bu repo büyüktür. **Hiçbir dokümanı ihtiyacın olmadan baştan sona okuma.**
Dokümanların çoğu birikimli defterdir; tamamını okumak bütçeyi bitirir.

**Oturum başında yalnız:** bu dosya · [`MEMORY.md`](MEMORY.md) · çalıştığın fazın
dokümanı (`docs/NN-*.md`) ve onun "Bu Faza Başlarken" listesi.

**Sonra, yalnız dokunduğun alan için:**

| İhtiyaç | Yol |
|---|---|
| Bir tuzak/desen var mı? | `MEMORY.md`'deki tabloyla [`docs/hafiza/<alan>.md`](docs/hafiza/) |
| Bir şey nerede yaşıyor? | [`docs/hafiza/kod-haritasi.md`](docs/hafiza/kod-haritasi.md) |
| Bir karar alınmış mı? | [`docs/KARARLAR-INDEKS.md`](docs/KARARLAR-INDEKS.md) → `grep -n "K-059" docs/KARARLAR.md` |
| Mimari resim (katman, veri modeli, çalıştırma yolu, güvenlik) | [`docs/MIMARI.md`](docs/MIMARI.md) — ilgili bölüm |
| MAF genişleme noktası | [`docs/MAF-GENISLEME-NOKTALARI.md`](docs/MAF-GENISLEME-NOKTALARI.md) |
| Geçmişte neden öyle yapıldı? | [`docs/arsiv/`](docs/arsiv/) — yalnız grep'le |
| Paketler, kurulum, faz durumu | [`README.md`](README.md) |

**`docs/KARARLAR.md` ve `docs/arsiv/*` hiçbir zaman baştan sona okunmaz.**
İndeksten satır numarasını bul, `sed -n 'N,Np'` ile o satırı oku. Aramak
okumaktan ucuzdur: `grep -rn "AsyncLocal" docs/hafiza/`.

---

## Temel İletişim Kuralları

**Kullanıcının dilinde yanıt ver.** Türkçe soruya Türkçe, diğer her dile
İngilizce. Kod ve commit mesajı hep İngilizce.

**Tüm yanıtlar ASD-STE100 Simplified Technical English kurallarına uyar** — kısa
cümle, tek fikir, aktif çatı, onaylı kelime. Türkçe yanıtta da geçerli; Türkçe'de
ç/ğ/ı/ö/ş/ü kullan.

**Teknik terimi çevirme.** Terim İngilizce kalır, Türkçe ek alır: `secret`,
`store`, `endpoint`, `scope`, `span`, `run`, `tool`. Çeviri kavramı
bulanıklaştırır: doğru "`secret` yazılmaz", yanlış "sır yazılmaz".

**Her belirsizliği sor.** Açık olmayan bir durum, edge-case veya tasarım kararı
çıktığında varsayım yapma; durumu tarif ederek kullanıcıya sor. Plan modundaysan
aklına takılan en küçük şeyi bile sor.

**Uzun vadeli mimari kararlar al.** Sonra değiştirilmek üzere tasarlanmış geçici
çözüm önerme.

**Karar defteri:** Daha önce reddedilmiş işleri yeniden önerme — önce
[`docs/KARARLAR-INDEKS-REDDEDILEN.md`](docs/KARARLAR-INDEKS-REDDEDILEN.md).

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

**Geliştirme fazlar hâlinde, çoğu zaman ayrı sohbetlerde yapılır.** Sonraki oturum
repo'yu sıfırdan okur ve yalnızca dokümanlara güvenir; bu yüzden her geliştirme
sonrası dokümanlar gözden geçirilir ve ahenkli tutulur.

| Kural | Neden |
|-------|-------|
| Doküman ile kod çelişirse **doküman yanlıştır** — koda göre düzeltilir | Sonraki oturum dokümana göre kod yazar |
| Plandan sapma **gizlenmez**, gerekçesiyle yazılır | Sapmanın gerekçesi en değerli bilgidir |
| Sonraki fazın dokümanı **devir teslim kalitesine** çıkarılır | Ayrı sohbet onunla tek başına çalışabilmeli |
| Her mimari karar `docs/KARARLAR.md`'ye numarayla ve gerekçeyle yazılır | Kapatılmış tartışma yeniden açılmaz |
| Keşfedilen tuzak **alan dosyasına** (`docs/hafiza/`) yazılır | `MEMORY.md` şişmez |
| Birikimli anlatı `docs/arsiv/`'e gider | Sıcak yol büyümezse her oturum ucuz başlar |

### Doküman bütçesi (zorunlu)

Sıcak yol dokümanları her oturumda okunur; büyümeleri her oturumu pahalılaştırır.
Bütçeler `scripts/dokuman-bakim.py` içindedir ve faz kapanışında denetlenir
(`python3 scripts/dokuman-bakim.py` — indeksi üretir + bütçeyi denetler). Bir
dosya bütçeyi aşarsa **içerik silinmez**: alan dosyasına veya `docs/arsiv/`'e
taşınır.

### Faz durumu

**Faz 0–56 tamam** (Faz 7 hariç, K-068); **57–59 planlandı**. Durum tablosu
[`README.md`](README.md)'de; faz sırası, migration numaraları ve açık kalemler
tur yol haritalarındadır ([ikinci](docs/IKINCI-FAZ-YOL-HARITASI.md) ·
[üçüncü](docs/UCUNCU-FAZ-YOL-HARITASI.md)), seçilmemiş kalemler
[adaylardadır](docs/UCUNCU-FAZ-ADAYLARI.md). Bu listeyi başka dosyada
tekrarlama — iki yerde tutmak kayma üretir.

Faz bittiğinde **`faz-tamamlama` skill'i uygulanır.** Atlanmaz.

---

## Doğrulama Kapıları

Dördü de sıfır uyarı vermelidir. Bir tanesi kırmızıysa iş **bitmemiştir**.

```bash
dotnet build  AgentPrism.slnx -c Release
dotnet test   AgentPrism.slnx -c Release --no-build
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes --no-restore
```

Ölçüldü: sıcak build ~5 sn. Kapılar ucuzdur — **erken ve sık çalıştır**.

> ⚠️ `dotnet format`, `dotnet build`'in yakalamadığı analyzer tanılarını
> yakalayabilir. Dört kapının da çalıştırılması bu yüzden zorunludur.

**Hızlı iç döngü.** Arayüze dokunmuyorsan `-p:AgentPrismFrontendEnabled=false`
npm/Vite/Vitest adımlarını atlar; tek test projesi
`dotnet test tests/<Proje> -c Release --no-build` ile koşar. Dört kapının tamamı
**faz kapanışında** ve arayüz/paket değişiminde çalışır.

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır. Bir analyzer
kuralını bastırmadan önce **neden** tetiklendiğini anla; bastırma gerekiyorsa
gerekçesini koda ve `docs/KARARLAR.md`'ye yaz.

**`secret` asla dosyaya yazılmaz.** Bağlantı dizesi ve API anahtarı yalnız
`dotnet user-secrets` içinde yaşar; `appsettings.json` boş placeholder taşır.
Faz sonunda `secret` taraması yapılır (`faz-tamamlama`).

`dotnet build` **arayüzü de derler**: `npm ci` → `tsc --noEmit` → Vitest → Vite →
Brotli → bundle bütçesi (250 KB gzip). Node.js 20.19+ gerekir.

---

## Skill'ler (Ortak İş Akışları)

Tekrarlanan iş akışları `.agents/skills/<ad>/SKILL.md` altındadır — talimat
burada tekrarlanmaz, skill okunup uygulanır. `faz-planlama` (aday F-NN faza
dönüşürken) · `faz-baslangic` (faza başlarken) · `faz-tamamlama` (kod bittiğinde)
· `maf-api-kesfi` (bir MAF tipini ilk kez kullanmadan önce).
Konvansiyon: [`.agents/skills/README.md`](.agents/skills/README.md).

---

## Kodlama Kuralları (Bu Repo'ya Özgü)

Genel .NET kuralları `.editorconfig`'dedir. Aşağıdakiler analyzer'ın
yakalayamadığı, projeye özgü kurallardır.

**🚨 Dil sınırı — pakete giren veya çalışma anında çalışan her şey İngilizce'dir.**
Kod, yorum, XML dokümanı, `exception`/log/`ProblemDetails` metni, migration
`.sql` yorumu, `template.json` açıklaması. Geliştirme aparatı (`docs/`,
`.agents/skills/`, `scripts/`) Türkçe kalır; `locales/tr.ts` meşru sözlüktür
(K-228). Kapı: `SourceLanguageTests` — taban çizgisi **yalnız küçülür**.

**MAF tiplerini sarmalama.** `AIAgent`, `AgentSession`, `ChatMessage`,
`AIFunction` doğrudan kullanılır. AgentPrism bir kontrol düzlemidir, bir
soyutlama katmanı değil.

**`Activity.Current` ve `AsyncLocal` async yardımcı metotta açılmaz.** Yazım
çağırana geri akmaz; `span`/`scope` çağıran metodun **kendi gövdesinde**
başlatılır ve akışlı yolda her `MoveNextAsync` öncesi tekrarlanır. Dört kez
yaşandı — `docs/hafiza/cekirdek-calistirma.md`.

**Tool'lar yalnızca kodda tanımlanır.** Arayüzden agent oluşturulabilir; tool
**kodu** yazılamaz. Bu bir güvenlik sınırıdır ve gevşetilmez.

**AOT uyumluluğu.** Listeyi `grep -l "AotCompatible>false" src/*/*.csproj` ile
doğrula. `reflection` kullanma; sırayla dene: (1) elle yaz; (2) `source generator`;
(3) kaçınılmazsa `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` işaretle —
uyarıyı **bastırma**, çağırana ilet.

**Ön sürüm MAF paketleri yalnızca `AgentPrism.AspNetCore` içinde** (K-008).

**`secret` veritabanına da yazılmaz** (K-059). Kayıtta yalnızca değerin
okunacağı **yapılandırma anahtarının adı** durur; değer çalışma anında
`IConfiguration` üzerinden çözülür.

**Gözlemlenebilirlik işlevselliği bozmaz.** `run` kaydı `store`'u hata verirse
`run` devam eder, hata loglanır.

**`ValueTask` dönen arayüzlerde `ConfigureAwait(false)`.** Kütüphane kodudur.

**Arayüz metni sözlükten gelir — iki dilde.** `locales/en.ts` anahtar kümesinin
kaynağıdır, `tr.ts` onu `Messages` tipiyle karşılar: eksik anahtar **derleme
hatasıdır** (K-228). Sunucu yanıtları çevrilmez (K-232).
Tuzaklar: `docs/hafiza/frontend.md`.

**İmza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır.** Yeni bir
alan/parametre eklerken çağrı zincirindeki her katmanın **gövdesini** elle izle.
Faz 20'de 1068 test bunu kaçırdı.

---

## Diyagram Kuralı

**Her diyagram Mermaid ile yazılır.** ASCII kutu çizimi (`┌─┐│└┘`) kullanılmaz —
elle hizalanır, bayatlar ve `git diff`'i bozar. Tip seçimi: katman/akış/karar
ağacı → `flowchart TD|LR` · çağrı sırası → `sequenceDiagram` · veri modeli →
`erDiagram` · durum makinesi → `stateDiagram-v2` · zaman planı → `gantt`.

- Türkçe etiket serbest; teknik terim orijinal dilinde kalır (`AIAgent`)
- Düğüm metninde `(`, `)`, `,`, `:` ayrıştırıcıyı bozar — tırnak kullan
- Bir diyagram **tek bir fikri** anlatır; on beş düğümü aşıyorsa ikiye böl
- Diyagram koddan sapmışsa **diyagram yanlıştır** — koda göre düzeltilir
- **İstisna:** dizin ağaçları düz metin kod bloğu kalır (`├──`, `└──`)
