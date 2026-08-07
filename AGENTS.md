# AgentPrism — AGENTS.md

> **Merkezi agent talimat dosyası.** Claude Code, GitHub Copilot, Antigravity ve diğer tüm AI kod agent'ları için tek kaynak budur. `CLAUDE.md` bu dosyaya symlink'tir — platform-spesifik ayrı talimat dosyası oluşturma; kural değişiklikleri yalnızca burada yapılır.

---

## Okuma Protokolü — Önce Bunu Uygula

Bu repo büyüktür. **Hiçbir dokümanı ihtiyacın olmadan baştan sona okuma.**
Dokümanların çoğu birikimli defterdir; tamamını okumak bütçeyi bitirir.

**Oturum başında yalnız şunlar:**

1. Bu dosya (zaten yüklü)
2. [`MEMORY.md`](MEMORY.md) — 4 KB, yönlendirme + her oturumda geçerli tuzaklar
3. Çalıştığın fazın dokümanı (`docs/NN-*.md`) ve onun "Bu Faza Başlarken" listesi

**Sonra, yalnız dokunduğun alan için:**

| İhtiyaç | Yol |
|---|---|
| Bir tuzak/desen var mı? | `MEMORY.md`'deki tabloyla `docs/hafiza/<alan>.md` |
| Bir karar alınmış mı? | [`docs/KARARLAR-INDEKS.md`](docs/KARARLAR-INDEKS.md) → `grep -n "K-059" docs/KARARLAR.md` |
| Mimari resim | [`docs/MIMARI.md`](docs/MIMARI.md) — ilgili bölüm |
| MAF genişleme noktası | [`docs/MAF-GENISLEME-NOKTALARI.md`](docs/MAF-GENISLEME-NOKTALARI.md) |
| Bir şey nerede yaşıyor? | [`docs/hafiza/kod-haritasi.md`](docs/hafiza/kod-haritasi.md) |
| Geçmişte neden öyle yapıldı? | `docs/arsiv/` — yalnız grep'le |

**`docs/KARARLAR.md` ve `docs/arsiv/*` hiçbir zaman baştan sona okunmaz.**
İndeksten satır numarasını bul, `sed -n 'N,Np'` ile o satırı oku. Aramak
okumaktan ucuzdur: `grep -rn "AsyncLocal" docs/hafiza/`.

---

## Temel İletişim Kuralları

**Kullanıcının dilinde yanıt ver.** Türkçe soruya Türkçe, diğer her dile İngilizce. Kod ve commit mesajı hep İngilizce.

**Tüm yanıtlar ASD-STE100 Simplified Technical English kurallarına uyar** — kısa cümle, tek fikir, aktif çatı, onaylı kelime. Türkçe yanıtta da geçerlidir; Türkçe'de ç/ğ/ı/ö/ş/ü kullan.

**Teknik terimi çevirme.** Terim İngilizce kalır, Türkçe ek alır: `secret`, `repository`, `store`, `storage`, `endpoint`, `scope`, `span`, `run`, `tool`. Çeviri kavramı bulanıklaştırır: doğru "`secret` yazılmaz", yanlış "sır yazılmaz".

**Geliştirme sırasında her belirsizliği sor.** Requirement'ta açık olmayan bir durum, edge-case veya tasarım kararı çıktığında varsayım yapmak yerine durumu tarif ederek kullanıcıya sor. Plan modundaysan aklına takılan en küçük şeyi bile sor.

**Uzun vadeli mimari kararlar al.** Sadece geçici çözümler sunan ve daha sonra değiştirilmesi amaçlanan çözümler önerme.

**Karar defteri:** Daha önce reddedilmiş işleri yeniden önerme — önce [`docs/KARARLAR-INDEKS-REDDEDILEN.md`](docs/KARARLAR-INDEKS-REDDEDILEN.md).

---

## Bu Proje Nedir

AgentPrism, Microsoft Agent Framework (MAF) üzerine kurulu bir **NuGet paket ailesidir**. Bir uygulama değil, milyonlarca geliştiricinin bağımlı olabileceği bir kütüphanedir. Bu, kod kalitesi eşiğini belirler:

- Public API'de kırıcı değişiklik pahalıdır — tasarımı ilk seferde doğru yap
- Her public üye XML dokümanına sahip olmalıdır (build bunu zorlar)
- Tüketicinin bağımlılık grafiğini kirletme
- `TryAdd*` ile kaydet; tüketicinin kaydı her zaman kazanmalı

Mimari resim: **[`docs/MIMARI.md`](docs/MIMARI.md)**.

---

## Faz Akışı ve Doküman Disiplini

**Geliştirme fazlar hâlinde ve çoğu zaman ayrı sohbetlerde yapılır.** Sonraki oturum bu repo'yu sıfırdan okur ve yalnızca dokümanlara güvenir.

> **Her geliştirme sonrası dokümanlar gözden geçirilir ve güncelliğini korur. Dokümanlar birbiriyle ahenk içinde olmalıdır.**

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
Bütçeler `scripts/dokuman-bakim.py` içinde tanımlıdır ve faz kapanışında denetlenir:

```bash
python3 scripts/dokuman-bakim.py     # KARARLAR indeksini üretir + bütçeyi denetler
```

Bir dosya bütçeyi aşarsa **içerik silinmez** — alan dosyasına veya `docs/arsiv/`'e taşınır.

### Faz durumu

**Faz 0–45 tamam** (Faz 7 hariç, K-068). Faz 46–52 planlandı, kodu yazılmadı
([üçüncü tur](docs/UCUNCU-FAZ-YOL-HARITASI.md)); seçilmemiş 20 kalem
[adaylardadır](docs/UCUNCU-FAZ-ADAYLARI.md).

Durum tablosu [`README.md`](README.md)'de, faz sırası ve migration numaraları
tur yol haritalarındadır ([ikinci](docs/IKINCI-FAZ-YOL-HARITASI.md) ·
[üçüncü](docs/UCUNCU-FAZ-YOL-HARITASI.md)); açık kalemler (Faz 7, Foundry,
`mssql/server`) o dosyalarda yazılıdır.
Bu listeyi başka dosyada tekrarlama — iki yerde tutmak kayma üretir.

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

### Hızlı iç döngü

Geliştirirken tüm çözümü her seferinde kurma: arayüze dokunmuyorsan
`-p:AgentPrismFrontendEnabled=false` npm/Vite/Vitest adımlarını atlar, tek test
projesi `dotnet test tests/<Proje> -c Release --no-build` ile koşar. Dört kapının
tamamı **faz kapanışında** ve arayüz/paket değişiminde çalışır.

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır. Bir analyzer kuralını bastırmadan önce **neden** tetiklendiğini anla; bastırma gerekiyorsa gerekçesini koda ve `docs/KARARLAR.md`'ye yaz.

**`secret` asla dosyaya yazılmaz.** Bağlantı dizesi ve API anahtarı yalnız `dotnet user-secrets` içinde yaşar. `appsettings.json` boş placeholder taşır. Faz sonunda `secret` taraması yapılır — komut `faz-tamamlama` skill'inde.

`dotnet build` **arayüzü de derler**: `npm ci` → `tsc --noEmit` → Vitest → Vite → Brotli → bundle bütçesi (250 KB gzip). Node.js 20.19+ gerekir.

> ⚠️ `dotnet format`, `dotnet build`'in yakalamadığı analyzer tanılarını yakalayabilir. Dört kapının da çalıştırılması bu yüzden zorunludur.

---

## Skill'ler (Ortak İş Akışları)

Tekrarlanan iş akışları `.agents/skills/<yetenek_adi>/SKILL.md` altında tanımlıdır — talimatlar bu dosyada tekrarlanmaz, ilgili skill okunup uygulanır:

| Skill | Ne zaman |
|-------|----------|
| `faz-planlama` | Bir aday kalem (F-NN) faza dönüşürken. Kanıtı doğrular, sınırları uygular, plan dokümanını yazar. |
| `faz-baslangic` | Bir faza başlarken. Minimum okuma kümesini ve sırayı verir. |
| `faz-tamamlama` | Bir fazın kodu bittiğinde. Doğrulama kapıları, doküman senkronizasyonu, karar defteri, hafıza. |
| `maf-api-kesfi` | MAF'ın bir tipini ilk kez kullanmadan önce. Gerçek imzayı reflection ile çıkarır. |

Klasör konvansiyonu ve taşınabilirlik: [`.agents/skills/README.md`](.agents/skills/README.md).

---

## Kodlama Kuralları (Bu Repo'ya Özgü)

Genel .NET kuralları `.editorconfig` içinde zorunlu kılınır. Aşağıdakiler analyzer'ın yakalayamadığı, projeye özgü kurallardır:

**MAF tiplerini sarmalama.** `AIAgent`, `AgentSession`, `ChatMessage`, `AIFunction` doğrudan kullanılır. AgentPrism bir kontrol düzlemidir, bir soyutlama katmanı değil.

**`Activity.Current` ve `AsyncLocal` async yardımcı metotta açılmaz.** Yazım çağırana geri akmaz; span/kapsam çağıran metodun kendi gövdesinde başlatılmalıdır. Akışlı yolda her `MoveNextAsync` öncesi tekrarlanır. Dört kez yaşandı (Faz 6, 11, 12, 15) — `docs/hafiza/cekirdek-calistirma.md`.

**Tool'lar yalnızca kodda tanımlanır.** Arayüzden agent oluşturulabilir; tool **kodu** yazılamaz. Bu bir güvenlik sınırıdır ve gevşetilmez.

**AOT uyumluluğu.** `Abstractions`, `Core`, `PostgreSql`, `OpenAI` paketleri AOT uyumludur. `reflection`'a dayanan API kullanma. Sırayla dene: (1) elle yaz; (2) `source generator` (`JsonSerializerContext`); (3) kaçınılmazsa `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` ile işaretle — uyarıyı **bastırma**, çağırana ilet.

**Ön sürüm MAF paketleri yalnızca `AgentPrism.AspNetCore` içinde.** Karar K-008.

**`secret` veritabanına da yazılmaz.** Bir `secret` gerekiyorsa kayıtta yalnızca değerin okunacağı **yapılandırma anahtarının adı** durur; değer çalışma anında `IConfiguration` üzerinden çözülür. Karar K-059.

**Gözlemlenebilirlik işlevselliği bozmaz.** `run` kaydı `store`'u hata verirse `run` devam eder; hata loglanır.

**`ValueTask` dönen arayüzlerde `ConfigureAwait(false)`.** Kütüphane kodudur.

**Arayüz metni sözlükten gelir — iki dilde.** `locales/en.ts` anahtar kümesinin
kaynağıdır, `tr.ts` onu `Messages` tipiyle karşılar: eksik anahtar **derleme
hatasıdır** (K-228). Sunucu yanıtları çevrilmez (K-232). Tuzaklar ve kurallar:
`docs/hafiza/frontend.md`.

**İmza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır.** Yeni bir alan/parametre eklerken çağrı zincirindeki her katmanın **gövdesini** elle izle. Faz 20'de 1068 test bunu kaçırdı.

---

## Diyagram Kuralı

**Her diyagram Mermaid ile yazılır.** ASCII kutu çizimi (`┌─┐│└┘`) kullanılmaz — elle hizalanır, bakım maliyeti yüzünden bayatlar ve `git diff`'i bozar.

Tip seçimi: katman/akış/karar ağacı → `flowchart TD|LR` · çağrı sırası →
`sequenceDiagram` · veri modeli → `erDiagram` · durum makinesi →
`stateDiagram-v2` · zaman planı → `gantt`.

- Türkçe etiket serbest; teknik terim orijinal dilinde kalır (`AIAgent`, `IRunStore`)
- Düğüm metninde `(`, `)`, `,`, `:` ayrıştırıcıyı bozar — tırnak kullan: `A["RunAsync(messages, session)"]`
- Bir diyagram **tek bir fikri** anlatır; on beş düğümü aşıyorsa ikiye böl
- Diyagram koddan sapmışsa **diyagram yanlıştır** — koda göre düzeltilir

**İstisna — dizin ağaçları.** Dosya/klasör listeleri düz metin kod bloğu olarak kalır (`├──`, `└──`).

---

## Canlı Referanslar

| Dosya | İçerik | Ne zaman okunur |
|-------|--------|-----------------|
| [`MEMORY.md`](MEMORY.md) | Yönlendirme + her oturumda geçerli tuzaklar | Her oturum |
| [`docs/hafiza/*.md`](docs/hafiza/) | Alan bazlı tuzak ve codepath notları | O alana dokunurken |
| [`docs/MIMARI.md`](docs/MIMARI.md) | Bugünkü mimari — katmanlar, veri modeli, çalıştırma yolu, güvenlik | İlgili bölüm |
| [`docs/KARARLAR-INDEKS.md`](docs/KARARLAR-INDEKS.md) | Kalıcı kararların indeksi (reddedilenler için bkz. üstteki "Karar defteri") | Karar ararken |
| `docs/KARARLAR.md` | Kararların tam gerekçesi | **Yalnız grep ile** |
| [`docs/MAF-GENISLEME-NOKTALARI.md`](docs/MAF-GENISLEME-NOKTALARI.md) | Kullandığımız/kullanmadığımız MAF noktaları | MAF'a dokunurken |
| `docs/NN-*.md` | Faz dokümanları — kapsam, DoD, devir teslim notları | O faz |
| `docs/arsiv/*.md` | Faz anlatısı, paket×faz birikimi | **Yalnız grep ile** |
| [`README.md`](README.md) | Dış yüzey — paketler, kurulum, yol haritası tablosu | Faz durumu |
