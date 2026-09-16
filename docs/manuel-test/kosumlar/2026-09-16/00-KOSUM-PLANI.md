# Koşum planı — tazelik ölçümü

> **Üretilir, elle yazılmaz.** Kaynak:
> `python3 scripts/manuel-test-tazelik.py --taban <commit>`.
>
> **Taban:** `12fb6477` · **Ölçülen:** `fdaab87c` · **Araya giren commit:** 630

## 1. Kovalar

| Kova | Case | Pay | Anlamı |
|---|---|---|---|
| **yeni** | 769 | %41 | Taban turdan sonra eklendi; hiç koşulmadı |
| **değişti** | 268 | %14 | Metni değişti; taban turun sonucu geçersiz |
| **sessiz** | 829 | %44 | Metni aynı; riski aile kaynağına giren commit sayısıdır |
| **toplam** | 1866 | | 36 aile |

🚨 **`sessiz` kovası güvenli demek değildir.** Metni değişmeyen bir
case, altındaki kod değiştiyse yanıltıcıdır — koşulmadan bayat olduğu
bilinemez. `kod kayması` sütunu o riski ölçer.

## 2. Aile ölçümü

| # | Aile | case | yeni | değişti | sessiz | kod kayması | risk |
|---|---|---|---|---|---|---|---|
| 01 | `01-KURULUM-VE-PAKETLEME.md` | 81 | 33 | 16 | 32 | 69 | 89 |
| 02 | `02-CEKIRDEK-VE-KATALOG.md` | 97 | 55 | 7 | 35 | 15 | 77 |
| 03 | `03-KALICILIK-POSTGRESQL.md` | 50 | 14 | 21 | 15 | 3 | 38 |
| 04 | `04-KALICILIK-DIGER.md` | 45 | 5 | 25 | 15 | 4 | 34 |
| 05 | `05-SAGLAYICI-OPENAI.md` | 40 | 0 | 13 | 27 | 2 | 15 |
| 06 | `06-SAGLAYICI-DIGER.md` | 39 | 0 | 9 | 30 | 1 | 10 |
| 07 | `07-HTTP-YONETIM-API.md` | 43 | 0 | 10 | 33 | 2 | 12 |
| 08 | `08-OPENAI-UYUMLU-UCLAR.md` | 50 | 1 | 6 | 43 | 1 | 8 |
| 09 | `09-ARAYUZ-GENEL.md` | 57 | 14 | 11 | 32 | 7 | 32 |
| 10 | `10-ARAYUZ-AGENT-PLAYGROUND.md` | 58 | 7 | 10 | 41 | 4 | 21 |
| 11 | `11-ARAYUZ-RUN-SESSION-SSE.md` | 66 | 20 | 13 | 33 | 4 | 37 |
| 12 | `12-GOZLEMLENEBILIRLIK-MALIYET.md` | 65 | 29 | 12 | 24 | 16 | 57 |
| 13 | `13-KIRACI-VE-GUVENLIK.md` | 144 | 90 | 9 | 45 | 5 | 104 |
| 14 | `14-SKILL-VE-SCRIPT.md` | 47 | 0 | 11 | 36 | 5 | 16 |
| 15 | `15-WORKFLOWS.md` | 70 | 10 | 9 | 51 | 6 | 25 |
| 16 | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` | 97 | 36 | 8 | 53 | 17 | 61 |
| 17 | `17-EVAL-VE-DENEYLER.md` | 69 | 0 | 12 | 57 | 8 | 20 |
| 18 | `18-MCP-VE-A2A.md` | 58 | 15 | 10 | 33 | 3 | 28 |
| 19 | `19-COK-MODLULUK-VE-SES.md` | 93 | 32 | 15 | 46 | 3 | 50 |
| 20 | `20-BELLEK-RAG-BAGLAM.md` | 31 | 0 | 8 | 23 | 3 | 11 |
| 21 | `21-DAYANIKLILIK-VE-IPTAL.md` | 55 | 27 | 7 | 21 | 7 | 41 |
| 22 | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` | 56 | 21 | 9 | 26 | 6 | 36 |
| 23 | `23-SAKLAMA-ARSIV-KOTA.md` | 45 | 19 | 7 | 19 | 3 | 29 |
| 24 | `24-TEST-PAKETI-VE-SABLON.md` | 66 | 25 | 6 | 35 | 9 | 40 |
| 25 | `25-SAGLIK-TESHIS-OPENAPI.md` | 45 | 17 | 4 | 24 | 5 | 26 |
| 26 | `26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` 🆕 | 16 | 16 | 0 | 0 | 2 | 18 |
| 27 | `27-MODEL-YEDEK-VE-ON-UCUS.md` 🆕 | 17 | 17 | 0 | 0 | 3 | 20 |
| 28 | `28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md` 🆕 | 10 | 10 | 0 | 0 | 3 | 13 |
| 29 | `29-AGENT-DESTEGI.md` 🆕 | 24 | 24 | 0 | 0 | 66 | 64 |
| 30 | `30-YEREL-REFERANS.md` 🆕 | 27 | 27 | 0 | 0 | 13 | 40 |
| 31 | `31-DOKUMAN-DOGRULUGU.md` 🆕 | 35 | 35 | 0 | 0 | 83 | 75 |
| 32 | `32-DOKUMAN-KALITESI.md` 🆕 | 40 | 40 | 0 | 0 | 43 | 80 |
| 33 | `33-DOKUMAN-KAPILARI.md` 🆕 | 25 | 25 | 0 | 0 | 75 | 65 |
| 34 | `34-ISTEMCI-VE-CLI.md` 🆕 | 46 | 46 | 0 | 0 | 11 | 57 |
| 35 | `35-TYPESCRIPT-ISTEMCISI.md` 🆕 | 11 | 11 | 0 | 0 | 23 | 34 |
| 36 | `36-GELISTIRME-KAPILARI.md` 🆕 | 48 | 48 | 0 | 0 | 86 | 88 |

🆕 = taban turda bu dosya yoktu.

## 3. Koşum sırası

**Zincir kapıdır.** `01 → 02 → 03 → 05 → 07` kırılırsa sonraki hiçbir
aile anlamlı sonuç vermez; bu beşi risk sırası **ezemez**. Kalan
aileler risk sırasındadır (`yeni + değişti + min(kod kayması, 40)`).

| Sıra | Aile | risk | Not |
|---|---|---|---|
| 1 | `01-KURULUM-VE-PAKETLEME.md` | 89 | 🔗 zincir |
| 2 | `02-CEKIRDEK-VE-KATALOG.md` | 77 | 🔗 zincir |
| 3 | `03-KALICILIK-POSTGRESQL.md` | 38 | 🔗 zincir |
| 4 | `05-SAGLAYICI-OPENAI.md` | 15 | 🔗 zincir |
| 5 | `07-HTTP-YONETIM-API.md` | 12 | 🔗 zincir |
| 6 | `13-KIRACI-VE-GUVENLIK.md` | 104 |  |
| 7 | `36-GELISTIRME-KAPILARI.md` | 88 |  |
| 8 | `32-DOKUMAN-KALITESI.md` | 80 |  |
| 9 | `31-DOKUMAN-DOGRULUGU.md` | 75 |  |
| 10 | `33-DOKUMAN-KAPILARI.md` | 65 |  |
| 11 | `29-AGENT-DESTEGI.md` | 64 |  |
| 12 | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` | 61 |  |
| 13 | `12-GOZLEMLENEBILIRLIK-MALIYET.md` | 57 |  |
| 14 | `34-ISTEMCI-VE-CLI.md` | 57 |  |
| 15 | `19-COK-MODLULUK-VE-SES.md` | 50 |  |
| 16 | `21-DAYANIKLILIK-VE-IPTAL.md` | 41 |  |
| 17 | `24-TEST-PAKETI-VE-SABLON.md` | 40 |  |
| 18 | `30-YEREL-REFERANS.md` | 40 |  |
| 19 | `11-ARAYUZ-RUN-SESSION-SSE.md` | 37 |  |
| 20 | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` | 36 |  |
| 21 | `04-KALICILIK-DIGER.md` | 34 |  |
| 22 | `35-TYPESCRIPT-ISTEMCISI.md` | 34 |  |
| 23 | `09-ARAYUZ-GENEL.md` | 32 |  |
| 24 | `23-SAKLAMA-ARSIV-KOTA.md` | 29 |  |
| 25 | `18-MCP-VE-A2A.md` | 28 |  |
| 26 | `25-SAGLIK-TESHIS-OPENAPI.md` | 26 |  |
| 27 | `15-WORKFLOWS.md` | 25 |  |
| 28 | `10-ARAYUZ-AGENT-PLAYGROUND.md` | 21 |  |
| 29 | `17-EVAL-VE-DENEYLER.md` | 20 |  |
| 30 | `27-MODEL-YEDEK-VE-ON-UCUS.md` | 20 |  |
| 31 | `26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` | 18 |  |
| 32 | `14-SKILL-VE-SCRIPT.md` | 16 |  |
| 33 | `28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md` | 13 |  |
| 34 | `20-BELLEK-RAG-BAGLAM.md` | 11 |  |
| 35 | `06-SAGLAYICI-DIGER.md` | 10 |  |
| 36 | `08-OPENAI-UYUMLU-UCLAR.md` | 8 |  |

## 4. Kapsama boşluğu

`CapabilityEntryPoints` kuralıyla (K-509) ölçüldü: **54**
kayıt giriş noktasının **0**'i manuel-test setinde hiç
anılmıyor.

Boşluk yok.

## 5. Çözülemeyen kaynak yolları

`00-INDEKS.md` §7 `Kaynak` sütunundan okunamayan belirteçler. Her biri
kod kaymasını **olduğundan küçük** gösterir ve aileyi risk sırasında
hak ettiği yerin altına düşürür.

Tümü çözüldü.
