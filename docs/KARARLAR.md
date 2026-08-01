# KARARLAR.md — Canlı Karar Defteri

> Bu dosya, projede **bilinçli olarak alınmış ve gerekçelendirilmiş kararların** kaydıdır. Amacı: gelecekteki bir oturumun (insan veya AI) daha önce kanıtla reddedilmiş bir işi yeniden önermesini veya kapatılmış bir tartışmayı yeniden açmasını engellemek.
>
> **Kullanım kuralı:** Buradaki bir kalemi öneri olarak gündeme getirmeden önce "yeniden açılma koşulu" sütununa bak. Koşul gerçekleşmediyse önerme. Koşul gerçekleştiyse, bu dosyayı güncelleyerek kalemi yeniden aç.

---

## 1. Kanıt Olmadan Yeniden Açılmayacak İşler

Kaynak: 2026 refaktör programı (Tur 1–4, `f2884d4..f2ba815`, 116 commit). Detaylı tarihçe: `claudedocs/arsiv/`.

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|


## 2. Kalıcı Mimari/Altyapı Kararları

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|


## 3. Yeni Karar Ekleme Şablonu

Bir iş "kanıt yok → yapma" ile kapatıldığında veya kalıcı bir mimari tercih yapıldığında ilgili tabloya satır ekle:

```
| <karar — ne yapılmadı/yapıldı> | YYYY-AA-GG | <gerekçe — hangi kanıt/ölçüm/kısıt> | <hangi koşul gerçekleşirse yeniden açılır> |
```

Kurallar:
- Gerekçesiz karar ekleme — "istemedik" yeterli değil; neden istenmediği yazılmalı.
- Bir kalem yeniden açıldığında satırı silme; sonuna "**(yeniden açıldı: YYYY-AA-GG, sebep)**" ekle ve işi normal akışta planla.
- Kullanıcı kararlarını `(kullanıcı kararı)` etiketiyle işaretle — bunlar teknik kanıtla değil, ancak kullanıcıyla konuşularak değişir.
