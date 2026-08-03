# AgentPrism — AGENTS.md

> **Merkezi agent talimat dosyası.** Claude Code, GitHub Copilot, Antigravity ve diğer tüm AI kod agent'ları için tek kaynak budur. `CLAUDE.md` bu dosyaya symlink'tir — platform-spesifik ayrı talimat dosyası oluşturma; kural değişiklikleri yalnızca burada yapılır.

---

## Okuma Protokolü — Önce Bunu Uygula

Bu depo büyüktür. **Hiçbir dokümanı ihtiyacın olmadan baştan sona okuma.**
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

**`docs/KARARLAR.md` (115 KB) ve `docs/arsiv/*` hiçbir zaman baştan sona okunmaz.**
İndeksten satır numarasını bul, `sed -n 'N,Np'` ile o satırı oku.

Aramak okumaktan ucuzdur:

```bash
grep -rn "AsyncLocal" docs/hafiza/
grep -n "jsonb" docs/KARARLAR.md
```

---

## Temel İletişim Kuralları

**Her zaman Türkçe konuş.** Kod, değişken adları, commit mesajları İngilizce kalabilir; ancak agent'ın tüm açıklamaları, soruları ve analizleri Türkçe olmalı. Tüm yanıtlarda **ASD-STE100 Basitleştirilmiş Teknik Dil kurallarını sıfır tolerans ile uygula** — kısa cümle, tek fikir, aktif çatı, onaylı kelime listesi. Bu kural hem Türkçe hem İngilizce yanıt için geçerlidir; her iki dilde de konuşulan dilin doğru karakterlerini kullan (ör. Türkçe'de ç/ğ/ı/ö/ş/ü). Teknik terimler orijinal dilinde kalır (ör. `AppService`, `migration`, `endpoint`).

**Geliştirme sırasında her belirsizliği sor.** Requirement'ta açık olmayan bir durum, edge-case veya tasarım kararı çıktığında varsayım yapmak yerine durumu tarif ederek kullanıcıya sor. Plan modundaysan aklına takılan en küçük şeyi bile sor.

**Uzun vadeli mimari kararlar al.** Sadece geçici çözümler sunan ve daha sonra değiştirilmesi amaçlanan çözümler önerme.

**Karar defteri:** Daha önce kanıtla reddedilmiş işleri yeniden önerme — önce [`docs/KARARLAR-INDEKS.md`](docs/KARARLAR-INDEKS.md).

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

**Geliştirme fazlar hâlinde ve çoğu zaman ayrı sohbetlerde yapılır.** Sonraki oturum bu depoyu sıfırdan okur ve yalnızca dokümanlara güvenir.

> **Her geliştirme sonrası dokümanlar gözden geçirilir ve güncelliğini korur. Dokümanlar birbiriyle ahenk içinde olmalıdır.**

| Kural | Neden |
|-------|-------|
| Doküman ile kod çelişirse **doküman yanlıştır** — koda göre düzeltilir | Sonraki oturum dokümana göre kod yazar |
| Plandan sapma **gizlenmez**, gerekçesiyle yazılır | Sapmanın gerekçesi en değerli bilgidir |
| Bir faz bitince sonraki fazın dokümanı **devir teslim kalitesine** çıkarılır | Ayrı sohbet o dokümanla tek başına çalışabilmeli |
| Her mimari karar `docs/KARARLAR.md`'ye numarayla ve gerekçeyle yazılır | Kapatılmış tartışma yeniden açılmaz |
| Keşfedilen tuzak **alan dosyasına** (`docs/hafiza/`) yazılır | Aynı tuzağa iki kez düşülmez, `MEMORY.md` şişmez |
| Birikimli anlatı `docs/arsiv/`'e gider, sıcak dokümana değil | Sıcak yol büyümezse her oturum ucuz başlar |

### Doküman bütçesi (zorunlu)

Sıcak yol dokümanları her oturumda okunur; büyümeleri her oturumu pahalılaştırır.
Bütçeler `scripts/dokuman-bakim.py` içinde tanımlıdır ve faz kapanışında denetlenir:

```bash
python3 scripts/dokuman-bakim.py     # KARARLAR indeksini üretir + bütçeyi denetler
```

Bir dosya bütçeyi aşarsa **içerik silinmez** — alan dosyasına veya `docs/arsiv/`'e taşınır.

### Faz durumu

**Sıradaki faz: 22** — [`docs/22-MCP-DERINLESMESI.md`](docs/22-MCP-DERINLESMESI.md).

Faz 0–21 tamamlandı; **Faz 7 beklemede** (yayın zamanı kullanıcı kararı, K-068).
Tam liste ve durum tablosu tek yerdedir: [`README.md`](README.md) yol haritası.
Faz 21–30 sırası, bağımlılıkları ve migration numaraları:
[`docs/IKINCI-FAZ-YOL-HARITASI.md`](docs/IKINCI-FAZ-YOL-HARITASI.md).
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

Ölçüldü (2026-08-03): sıcak build ~5 sn, 1070 test ~32 sn. Kapılar ucuzdur —
**erken ve sık çalıştır**, faz sonuna biriktirme.

### Hızlı iç döngü

Geliştirirken her seferinde tüm çözümü kurma:

```bash
# arayüze dokunmuyorsan (npm/Vite/Vitest adımlarını atlar)
dotnet build AgentPrism.slnx -c Release -p:AgentPrismFrontendEnabled=false

# yalnız ilgili test projesi
dotnet test tests/AgentPrism.Core.UnitTests -c Release --no-build
```

Dört kapının tamamı **faz kapanışında** ve arayüz/paket değişiminde çalışır.

`TreatWarningsAsErrors` açıktır — uyarı yoktur, hata vardır. Bir analyzer kuralını bastırmadan önce **neden** tetiklendiğini anla; bastırma gerekiyorsa gerekçesini koda ve `docs/KARARLAR.md`'ye yaz.

**Sırlar asla dosyaya yazılmaz.** Bağlantı dizesi ve API anahtarı yalnız `dotnet user-secrets` içinde yaşar. `appsettings.json` boş placeholder taşır. Faz sonunda sır taraması yapılır — komut `faz-tamamlama` skill'inde.

`dotnet build` **arayüzü de derler**: `npm ci` → `tsc --noEmit` → Vitest → Vite → Brotli → bundle bütçesi kapısı (250 KB gzip). Node.js 20.19+ gerekir.

> ⚠️ `dotnet format`, `dotnet build`'in yakalamadığı analyzer tanılarını yakalayabilir. Dört kapının da çalıştırılması bu yüzden zorunludur.

---

## Skill'ler (Ortak İş Akışları)

Tekrarlanan iş akışları `.agents/skills/<yetenek_adi>/SKILL.md` altında tanımlıdır — talimatlar bu dosyada tekrarlanmaz, ilgili skill okunup uygulanır:

| Skill | Ne zaman |
|-------|----------|
| `faz-baslangic` | Bir faza başlarken. Minimum okuma kümesini ve sırayı verir. |
| `faz-tamamlama` | Bir fazın kodu bittiğinde. Doğrulama kapıları, doküman senkronizasyonu, karar defteri, hafıza. |
| `maf-api-kesfi` | MAF'ın bir tipini ilk kez kullanmadan önce. Gerçek imzayı reflection ile çıkarır. |

Klasör konvansiyonu: her skill'de `SKILL.md` zorunlu (frontmatter: `name`, `description`); gerektiğinde `scripts/`, `examples/`, `resources/`, `references/` eklenebilir. Skill mekanizması olmayan agent'lar (Copilot vb.) `SKILL.md`'yi normal doküman gibi okuyup uygular. Claude Code keşfi için `.claude/skills` → `.agents/skills` symlink'tir.

---

## Kodlama Kuralları (Bu Depoya Özgü)

Genel .NET kuralları `.editorconfig` içinde zorunlu kılınır. Aşağıdakiler analyzer'ın yakalayamadığı, projeye özgü kurallardır:

**MAF tiplerini sarmalama.** `AIAgent`, `AgentSession`, `ChatMessage`, `AIFunction` doğrudan kullanılır. AgentPrism bir kontrol düzlemidir, bir soyutlama katmanı değil.

**`Activity.Current` ve `AsyncLocal` async yardımcı metotta açılmaz.** Yazım çağırana geri akmaz; span/kapsam çağıran metodun kendi gövdesinde başlatılmalıdır. Akışlı yolda her `MoveNextAsync` öncesi tekrarlanır. Dört kez yaşandı (Faz 6, 11, 12, 15) — `docs/hafiza/cekirdek-calistirma.md`.

**Tool'lar yalnızca kodda tanımlanır.** Arayüzden agent oluşturulabilir; tool **kodu** yazılamaz. Bu bir güvenlik sınırıdır ve gevşetilmez.

**AOT uyumluluğu.** `Abstractions`, `Core`, `PostgreSql`, `OpenAI` paketleri AOT uyumludur. Yansımaya dayanan API kullanma. Sırayla dene: (1) elle yaz; (2) kaynak üreteci (`JsonSerializerContext`); (3) kaçınılmazsa `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` ile işaretle — uyarıyı **bastırma**, çağırana ilet.

**Ön sürüm MAF paketleri yalnızca `AgentPrism.AspNetCore` içinde.** Karar K-008.

**Sırlar veritabanına da yazılmaz.** Bir sır gerekiyorsa kayıtta yalnızca değerin okunacağı **yapılandırma anahtarının adı** durur; değer çalışma anında `IConfiguration` üzerinden çözülür. Karar K-059.

**Gözlemlenebilirlik işlevselliği bozmaz.** Çalıştırma kaydı deposu hata verirse çalıştırma devam eder; hata loglanır.

**`ValueTask` dönen arayüzlerde `ConfigureAwait(false)`.** Kütüphane kodudur.

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
| [`docs/KARARLAR-INDEKS.md`](docs/KARARLAR-INDEKS.md) | 181 kararın tek satırlık indeksi | Karar ararken |
| `docs/KARARLAR.md` | Kararların tam gerekçesi | **Yalnız grep ile** |
| [`docs/MAF-GENISLEME-NOKTALARI.md`](docs/MAF-GENISLEME-NOKTALARI.md) | Kullandığımız/kullanmadığımız MAF noktaları | MAF'a dokunurken |
| `docs/NN-*.md` | Faz dokümanları — kapsam, DoD, devir teslim notları | O faz |
| `docs/arsiv/*.md` | Faz anlatısı, paket×faz birikimi | **Yalnız grep ile** |
| [`README.md`](README.md) | Dış yüzey — paketler, kurulum, yol haritası tablosu | Faz durumu |
