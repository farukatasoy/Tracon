# Skill'ler — Klasör Konvansiyonu

Tekrarlanan iş akışları burada yaşar. Hangi skill'in ne zaman çalıştırılacağı
[`AGENTS.md`](../../AGENTS.md) içindeki tablodadır; bu dosya yalnız **klasör
kurallarını** anlatır.

## Kurallar

- Her skill bir klasördür: `.agents/skills/<yetenek-adi>/`
- `SKILL.md` **zorunludur**. Frontmatter iki alan taşır: `name`, `description`
- `name` klasör adıyla birebir aynıdır ve kebab-case yazılır
- `description` skill'in **ne zaman** kullanılacağını söyler, ne yaptığını değil.
  Agent bu cümleye bakarak skill'i seçer; belirsiz bir açıklama skill'i
  görünmez yapar
- Gerektiğinde alt klasör eklenir: `scripts/`, `examples/`, `resources/`,
  `references/`

## Taşınabilirlik

Skill mekanizması olmayan agent'lar (GitHub Copilot vb.) `SKILL.md`'yi normal
bir doküman gibi okuyup uygular. Bu yüzden `SKILL.md` **kendi kendine yeten**
bir metin olmalıdır; bir `runtime`'a bağlı yazılmaz.

Claude Code keşfi için `.claude/skills` → `.agents/skills` symlink'tir.

## Mevcut skill'ler ve zincir

```
aday listesi (F-NN)  →  faz-planlama  →  docs/NN-*.md  →  faz-baslangic  →  kod  →  faz-tamamlama
                                                                             ↑
                                                              maf-api-kesfi (MAF tipi kullanmadan önce)
```
