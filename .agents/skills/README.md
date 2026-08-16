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
aday-kesfi  →  aday listesi (F-NN)
                      │
                      ▼
faz-planlama  →  docs/NN-*.md  →  faz-baslangic  →  faz-uygulama  →  faz-denetim  →  faz-tamamlama
                                                          ↑                                  │
                                       maf-api-kesfi (MAF tipi kullanmadan önce)              ▼
                                                                                 docs-site + manuel-test

kusur-giderme — zincirin dışındadır, bir kusur bulunduğunda her an koşar
```

| Skill | Ne zaman |
|---|---|
| `aday-kesfi` | Yeni aday yeteneği ararken — **yalnız kullanıcı istediğinde** |
| `faz-planlama` | Aday (F-NN) faz dokümanına dönüşürken |
| `faz-baslangic` | Faza başlarken — okuma protokolü |
| `faz-uygulama` | İlk kod satırından önce — yazım protokolü |
| `faz-denetim` | Kod bittiğinde — taze bağlamlı bağımsız denetim |
| `faz-tamamlama` | Kapanışta — kapılar, manuel case, site, doküman |
| `maf-api-kesfi` | Bir MAF tipini ilk kez kullanmadan önce |
| `kusur-giderme` | Bir kusur bulunduğunda — her an |
