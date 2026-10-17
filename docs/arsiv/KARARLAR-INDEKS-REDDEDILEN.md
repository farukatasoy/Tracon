# KARARLAR — Reddedilen İşler

> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

Daha önce kanıtla reddedilmiş işlerin kontrol listesi — **bunları yeniden önerme.**
Kalıcı (K-NNN) kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).

```bash
grep -n 'Blazor' docs/KARARLAR.md   # başlıktan bir sözcükle tam gerekçe
```

## Reddedilen İşler (24 kalem)

| Karar |
|---|
| Arayüz Blazor ile yazılmadı 👤 |
| EF Core kullanılmadı 👤 |
| `netstandard2.0` ve `net472` hedeflenmedi |
| Tek paket (monolitik) paketleme yapılmadı 👤 |
| Geçişli sabitleme (`CentralPackageTransitivePinningEnabled`) açılmadı |
| Arayüzden tool kodu yazma özelliği eklenmedi |
| MAF tipleri sarmalanmadı |
| Trim/AOT analyzer'ları kök seviyede açılmadı |
| Katalog MAF'ın `AddAIAgent` kayıtlarını doğrudan okumadı |
| Yerleşik OpenAI model listesi kodda tutulmadı |
| Responses API'de sunucu tarafı konuşma durumu kullanılmadı |
| `ValidateDataAnnotations()` kullanılmadı |
| Çalıştırma kaydı MAF middleware'i olarak yazılmadı |
| EF Core migration'ları yerine gömülü SQL — uygulandı ve doğrulandı |
| `tenant_id` sütunlarına yabancı anahtar konmadı |
| MAF'ın `MapOpenAIResponses()` / `MapOpenAIConversations()` uçları kullanılmadı 👤 |
| OpenAPI paketi `Tracon.AspNetCore` bağımlılığı yapılmadı |
| `run_events` gerçekten partition'lanmadı |
| `MigrationRunner`'a deadlock (SQL Server hata 1205) için yeniden deneme eklenmedi (Faz 63) |
| Tam test koşumunda `-maxcpucount:1` gevşetilmedi (iki dalgalı koşum) |
| Eval suite'i için ayrı bir CI kapısı eklenmedi — yetenek zaten var |
| Skor düşüşü için ayrı bir alarm eklenmedi — yetenek zaten var |
| Agent sürümüne `production`/`staging` label'ı eklenmedi — ihtiyaç zaten karşılanıyor |
| Langfuse'un beş sütunu ayrı bir katman olarak kurulmadı — beşi de zaten var |
