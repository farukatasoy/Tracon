# docs/kesif/ — Aday Keşfi Turlarının Kaydı

Bu dizin `aday-kesfi` skill'inin oturum notlarını taşır. Her dosya bir turun
kaydıdır: `YYYY-AA-GG-<konu>.md`.

**Bu dosyalar spesifikasyon değildir.** Hiçbir oturum burayı baştan sona
okumaz; yalnız "bu fikir daha önce konuşulmuş muydu" sorusu için `grep`'lenir.

| Nerede ne yaşar | |
|---|---|
| Turun tamamı — ham liste, eleme, kanıt, ret gerekçeleri | **burada** |
| Onaylanan kalemin tam metni (F-NN) | [`../ADAYLAR.md`](../ADAYLAR.md) |
| Kalemin uygulanabilir planı | `docs/NN-*.md` — `faz-planlama` yazar |
| Kalemleri tükenmiş turlar | git show 2a8149ef:docs/arsiv/kesif/INDEKS.md · 2026-10-17'de silinen iki tur (`2026-08-26-ef-core-uyum-olcumu.md`, `2026-09-07-langfuse-esinli-tur.md`): `git show 55584bb6:docs/kesif/<dosya>` |

Dizin, doküman dizin bütçesinden **hariç** tutulur (K-426; K-412 deseni):
bir koşumun kaydıdır, oturum maliyeti değildir. Bu yüzden birikebilir.

Şablon: [`.agents/skills/aday-kesfi/resources/kesif-notu-sablonu.md`](../../.agents/skills/aday-kesfi/resources/kesif-notu-sablonu.md).
