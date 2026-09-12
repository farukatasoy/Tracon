# KARARLAR — Reddedilen İşler

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

Daha önce kanıtla reddedilmiş işlerin kontrol listesi — **bunları yeniden önerme.**
Kalıcı (K-NNN) kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).

```bash
sed -n '120,121p' docs/KARARLAR.md   # satır numarasıyla tam gerekçe
```

## Reddedilen İşler (26 kalem)

| KARARLAR.md satırı | Karar |
|---|---|
| L15 | Arayüz Blazor ile yazılmadı 👤 |
| L16 | EF Core kullanılmadı 👤 |
| L17 | `netstandard2.0` ve `net472` hedeflenmedi |
| L18 | Tek paket (monolitik) paketleme yapılmadı 👤 |
| L19 | Geçişli sabitleme (`CentralPackageTransitivePinningEnabled`) açılmadı |
| L20 | Arayüzden tool kodu yazma özelliği eklenmedi |
| L21 | MAF tipleri sarmalanmadı |
| L22 | Trim/AOT analyzer'ları kök seviyede açılmadı |
| L23 | Katalog MAF'ın `AddAIAgent` kayıtlarını doğrudan okumadı |
| L24 | `AddToolsFrom<T>()` (attribute taramalı tool kaydı) Faz 1'de yapılmadı 🔁 |
| L25 | Yerleşik OpenAI model listesi kodda tutulmadı |
| L26 | Responses API'de sunucu tarafı konuşma durumu kullanılmadı |
| L27 | `ValidateDataAnnotations()` kullanılmadı |
| L28 | Çalıştırma kaydı MAF middleware'i olarak yazılmadı |
| L29 | EF Core migration'ları yerine gömülü SQL — uygulandı ve doğrulandı |
| L30 | `tenant_id` sütunlarına yabancı anahtar konmadı |
| L31 | `tool_invocations` tablosu Faz 2'de doldurulmadı |
| L32 | MAF'ın `MapOpenAIResponses()` / `MapOpenAIConversations()` uçları kullanılmadı 👤 |
| L33 | `/v1/conversations` ucu Faz 4'te yazılmadı 👤🔁 |
| L34 | `/api/stats` maliyet döndürmüyor |
| L35 | OpenAPI paketi `Tracon.AspNetCore` bağımlılığı yapılmadı |
| L36 | `run_events` gerçekten partition'lanmadı |
| L37 | Arayüzden tool istatistiği ve model sağlık kontrolü Faz 5'te gösterilmedi 👤 |
| L38 | Arayüz i18n altyapısı kurulmadı; dil İngilizce 👤🔁 |
| L39 | `MigrationRunner`'a deadlock (SQL Server hata 1205) için yeniden deneme eklenmedi (Faz 63) |
| L40 | Tam test koşumunda `-maxcpucount:1` gevşetilmedi (iki dalgalı koşum) |
