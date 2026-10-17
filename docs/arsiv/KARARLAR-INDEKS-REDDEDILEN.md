# KARARLAR — Reddedilen İşler

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

Daha önce kanıtla reddedilmiş işlerin kontrol listesi — **bunları yeniden önerme.**
Kalıcı (K-NNN) kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).

```bash
sed -n '120,121p' docs/KARARLAR.md   # satır numarasıyla tam gerekçe
```

## Reddedilen İşler (24 kalem)

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
| L24 | Yerleşik OpenAI model listesi kodda tutulmadı |
| L25 | Responses API'de sunucu tarafı konuşma durumu kullanılmadı |
| L26 | `ValidateDataAnnotations()` kullanılmadı |
| L27 | Çalıştırma kaydı MAF middleware'i olarak yazılmadı |
| L28 | EF Core migration'ları yerine gömülü SQL — uygulandı ve doğrulandı |
| L29 | `tenant_id` sütunlarına yabancı anahtar konmadı |
| L30 | MAF'ın `MapOpenAIResponses()` / `MapOpenAIConversations()` uçları kullanılmadı 👤 |
| L31 | OpenAPI paketi `Tracon.AspNetCore` bağımlılığı yapılmadı |
| L32 | `run_events` gerçekten partition'lanmadı |
| L33 | `MigrationRunner`'a deadlock (SQL Server hata 1205) için yeniden deneme eklenmedi (Faz 63) |
| L34 | Tam test koşumunda `-maxcpucount:1` gevşetilmedi (iki dalgalı koşum) |
| L35 | Eval suite'i için ayrı bir CI kapısı eklenmedi — yetenek zaten var |
| L36 | Skor düşüşü için ayrı bir alarm eklenmedi — yetenek zaten var |
| L37 | Agent sürümüne `production`/`staging` label'ı eklenmedi — ihtiyaç zaten karşılanıyor |
| L38 | Langfuse'un beş sütunu ayrı bir katman olarak kurulmadı — beşi de zaten var |
