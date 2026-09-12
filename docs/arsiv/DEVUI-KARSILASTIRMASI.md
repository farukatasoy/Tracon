# DevUI Karsilastirmasi — Tracon'in Gerekcesi

> `MIMARI.md`'den tasindi (2026-08-04, Faz 23): sicak yol butcesi.
> Bu bolum **tarihsel gerekcedir** ve degismez; bugunku mimari `MIMARI.md`'dedir.

## Neden Tracon?

Microsoft Agent Framework (MAF) 1.16.0 ile GA oldu. Güçlü bir agent runtime sunar. Ancak resmî geliştirici arayüzü **DevUI** hâlâ preview ve dokümanı açıkça şunu söyler:

> "DevUI is a **sample app** to help you visualize and debug your agents and workflows during development. It is **not** intended for production use."

DevUI'nin kaynak kodundan doğrulanan sınırları:

| Sınır | Kanıt |
|-------|-------|
| Kalıcılık yok | `Hosting.OpenAI/ServiceCollectionExtensions.cs` yalnız `InMemoryConversationStorage`, `InMemoryAgentConversationIndex`, `InMemoryResponsesService` kaydeder |
| Erişim kilitli | `DevUIAuthFilter` loopback dışı istekleri 403 döner; token tek sabit değer |
| Agent yönetimi yok | `/v1/entities` ve `/v1/entities/{id}/info` salt okunur |
| .NET dokümanı yok | Learn sayfası C# pivotunda "Coming Soon" |
| PostgreSQL yok | Kalıcılık paketleri yalnız `CosmosNoSql` ve `Valkey` |

**Tracon bu boşluğu doldurur.** DevUI'nin yerine geçmez — DevUI'nin bıraktığı yerden devam eder.

| | DevUI | Tracon |
|---|-------|------------|
| Amaç | Geliştirme sırasında görselleştirme | Üretimde çalışan kontrol düzlemi |
| Kalıcılık | Bellek içi | PostgreSQL (`tracon` şeması) |
| Erişim | Loopback + sabit token | Loopback + token + authorization policy |
| Agent tanımı | Salt okunur | Kod + veritabanı, versiyonlu, geri alınabilir |
| Çok kiracılılık | Yok | `tenant_id` ile her sorguda |
| Denetim izi | Yok | `audit_log` |

---
