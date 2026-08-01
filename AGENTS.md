# AgentPrism — AGENTS.md

> **Merkezi agent talimat dosyası.** Claude Code, GitHub Copilot, Antigravity ve diğer tüm AI kod agent'ları için tek kaynak budur. `CLAUDE.md` bu dosyaya symlink'tir — platform-spesifik ayrı talimat dosyası oluşturma; kural değişiklikleri yalnızca burada yapılır.

## Memory

Read the first 200 lines of MEMORY.md before beginning.

Update MEMORY.md as you discover codepaths, patterns, library locations, and key architectural decisions. This builds up institutional knowledge across conversations. Write concise notes about what you found.

---

## Temel İletişim Kuralları

**Her zaman Türkçe konuş.** Kod, değişken adları, commit mesajları İngilizce kalabilir; ancak agent'ın tüm açıklamaları, soruları ve analizleri Türkçe olmalı. Tüm yanıtlarda **ASD-STE100 Basitleştirilmiş Teknik Dil kurallarını sıfır tolerans ile uygula** — kısa cümle, tek fikir, aktif çatı, onaylı kelime listesi. Bu kural hem Türkçe hem İngilizce yanıt için geçerlidir; her iki dilde de konuşulan dilin doğru karakterlerini kullan (ör. Türkçe'de ç/ğ/ı/ö/ş/ü). Teknik terimler orijinal dilinde kalır (ör. `AppService`, `migration`, `endpoint`).

**Geliştirme sırasında her belirsizliği sor.** Requirement'ta açık olmayan bir durum, edge-case veya tasarım kararı çıktığında varsayım yapmak yerine durumu tarif ederek kullanıcıya sor. Plan modundaysan aklına takılan en küçük şeyi bile sor.

**Uzun vadeli mimari kararlar al.** Sadece geçici çözümler sunan ve daha sonra değiştirilmesi amaçlanan çözümler önerme.

**Karar defteri:** Daha önce kanıtla reddedilmiş işleri yeniden önerme — `docs/KARARLAR.md`'ye bak (rate limiting, output caching, Value Object'ler, Redis/yatay ölçekleme vb. orada gerekçeleriyle kapalı).

---

## Skill'ler (Ortak İş Akışları)

Tekrarlanan iş akışları `.agents/skills/<yetenek_adi>/SKILL.md` altında tanımlıdır — talimatlar bu dosyada tekrarlanmaz, ilgili skill okunup uygulanır:

| Skill | Ne zaman |
|-------|----------|


Klasör konvansiyonu: her skill'de `SKILL.md` zorunlu (frontmatter: `name`, `description`); gerektiğinde `scripts/`, `examples/`, `resources/`, `references/` (>500 satır ek dokümantasyon) alt klasörleri eklenebilir. Skill mekanizması olmayan agent'lar (Copilot vb.) ilgili `SKILL.md`'yi normal doküman gibi okuyup uygular. Claude Code keşfi için `.claude/skills` → `.agents/skills` symlink'tir.

---


## Canlı Referanslar

| Dosya | İçerik |
|-------|--------|
| `MEMORY.md` | Oturumlar arası biriken kurumsal bilgi — codepath'ler, desenler, keşifler |
| `docs/KARARLAR.md` | Karar defteri — reddedilen işler + kalıcı tercihler, gerekçeleriyle |
