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

```mermaid
flowchart LR
    accTitle: Skill zinciri ve zincir disi skiller
    accDescr: Aday kesfi aday listesi uretir; faz planlama onu faz dokumanina cevirir; baslangic, uygulama, denetim ve tamamlama sirayla kosar; tamamlama tuketici dokuman senkronunu cagirir. Uc skill zincirin disindadir ve kesikli ok ile baglanir.
    A["aday-kesfi<br/>aday listesi F-NN"] --> B["faz-planlama<br/>docs/NN-*.md"]
    B --> C["faz-baslangic"] --> D["faz-uygulama"] --> E["faz-denetim"] --> F["faz-tamamlama"]
    F --> G["tuketici-dokuman-senkronu<br/>site · sevk edilen metin · yerel referans"]
    M["maf-api-kesfi"] -.->|"MAF tipini ilk kez kullanmadan once"| D
    K["kusur-giderme"] -.->|"bir kusur bulundugunda, her an"| D
    T["manuel-test-kosumu"] -.->|"surum oncesi tam set kosumu"| F
```

| Skill | Ne zaman |
|---|---|
| `aday-kesfi` | Yeni aday yeteneği ararken — **yalnız kullanıcı istediğinde** |
| `faz-planlama` | Aday (F-NN) faz dokümanına dönüşürken |
| `faz-baslangic` | Faza başlarken — okuma protokolü |
| `faz-uygulama` | İlk kod satırından önce — yazım protokolü |
| `faz-denetim` | Kod bittiğinde — taze bağlamlı bağımsız denetim |
| `faz-tamamlama` | Kapanışta — kapılar, manuel case, doküman senkronu |
| `tuketici-dokuman-senkronu` | Faz kullanıcıya dönük bir yüzeye dokunduğunda — `faz-tamamlama` Adım 7 içinden |
| `maf-api-kesfi` | Bir MAF tipini ilk kez kullanmadan önce |
| `kusur-giderme` | Bir kusur bulunduğunda — her an |
| `manuel-test-kosumu` | `docs/manuel-test/` setinin **tamamı** koşulurken ve kusurları kapatılırken |
