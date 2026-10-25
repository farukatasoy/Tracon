# Faz Yol Haritası

> **Üretilen dosya. Elle düzenleme.** Kaynak: açık fazların kendi
> dokümanındaki `> **Durum:**` satırı ve kapanan fazlar için
> [`FAZ-DEFTERI.md`](FAZ-DEFTERI.md). Yeniden üretmek için:
> `python3 scripts/dokuman-bakim.py`

Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;
bu dosyayı düzeltmek bir sonraki üretimde geri alınır.

**Sıradaki boş faz numarası: 246.**

## Açık fazlar (6 kalem)

| Faz | Konu | Durum |
|-----|------|-------|
| [193](193-NET8-NET9-DUSURME.md) | `net8.0` ve `net9.0` Hedeflerinin Düşürülmesi | 📋 Planlandı |
| [241](241-LOOP-ITERASYON-CERCEVESI.md) | Native Agent Akışında Loop İterasyon Sınırı | 📋 Planlandı |
| [242](242-AGENT-ONAY-OLCUTU.md) | Agent Onay Ölçütü (`agentReview`) ve Loop Denetim İzi | 📋 Planlandı |
| [243](243-TOOL-BUTCE-KAPISI.md) | Bütçe Tükenince Tool Çağrısı Başlamaz | 📋 Planlandı |
| [244](244-GEMINI-KONUSMA-ADAPTORU.md) | Gemini Konuşma Adaptörü ve Diyalog Yeteneğinin Görünürlüğü | 📋 Planlandı |
| [245](245-OPENAI-STRICT-SEMA.md) | OpenAI Strict Şema Modu ve Sağlayıcıya Giden Etkin Şema | 📋 Planlandı |

Kapanan fazlar (240 kayıt; son: Faz 240 — Kiracı Başına Workflow Eşzamanlılığı, `Queued` Bekleme ve Adil Kiralama): [`FAZ-DEFTERI.md`](FAZ-DEFTERI.md). Kapanan faz dokümanı silinir; tam metne defterdeki `git show` işaretçisi götürür. Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md).
