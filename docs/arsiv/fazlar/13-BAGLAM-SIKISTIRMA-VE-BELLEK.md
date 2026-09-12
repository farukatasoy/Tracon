# Faz 13 — Bağlam Sıkıştırma ve Bellek Sağlayıcıları

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-11**
> **Önkoşul:** Yok (Faz 10 ve 12 bu fazı **gerekli** kılar — bağlam onlarla büyür)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok (ölçüldü — bkz. §13.4) · **Migration:** Yok

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/13-BAGLAM-SIKISTIRMA-VE-BELLEK.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Faz 12'den Devralınanlar

### Sağlayıcı listesi hazır

`AgentDefinitionCompiler.CompileChatAgent` bir sağlayıcı **listesi** kurar.
`CompactionProvider` ve bellek sağlayıcıları (`FileMemoryProvider`,
`TodoProvider`, `TextSearchProvider`) bu listeye eklendi — aşağıya bakın.

### Çalıştırma kapsamı

```csharp
TraconRunContext.Current            // okuma
TraconRunContext.SetCurrent(scope)  // yazma
```

`AgentRunScope` bu fazda yeni bir alan kazandı: `internal CompactionUsageAccumulator? ExtraUsage`
(bkz. §13.2). Sıkıştırma olayını akışa yazmanın **tek yolu** hâlâ
`TraconRunContext.Current?.Writer`'dır.

### 🚨 Bilinen tuzaklar (Faz 12'de ölçüldü, bu fazda da geçerli kaldı)

| Tuzak | Kural |
|-------|-------|
| `async IAsyncEnumerable` gövdesinde yapılan `AsyncLocal` ataması **`yield return` sınırını aşmaz** | Kapsam her `MoveNextAsync`'ten **hemen önce** yeniden yazılmalıdır. |
| MAF alt agent'ı `options = null` ile çağırır | Bir `AIContextProvider`'ın açtığı tool'dan tetiklenen çağrılarda gelen ayarlara güvenilemez. |

### 🚨 Bu fazda keşfedilen yeni tuzaklar

| Tuzak | Kural |
|-------|-------|
| `CompactionStrategy.CompactCoreAsync` **korumalıdır** ve C#'ta bir kardeş türetilmiş tipin örneği üzerinden çağrılamaz | Bir strateji sarmalayıcısı (`ObservedCompactionStrategy`) iç stratejiyi **`CompactAsync`** (public, sanal olmayan) ile çağırır. `CompactAsync` iç stratejinin **kendi** tetikleyicisini tekrar kontrol eder — bu yüzden iç strateji de dış ile **aynı** tetikleyiciyle kurulur (`ContextWindow`/`Pipeline` hariç, onlar kendi iç tetikleyicilerini kendileri taşır). |
| `AgentDefinitionPayload` (`Tracon.PostgreSql`) `AgentDefinition`'ı **ayrı bir DTO** ile serileştirir, kaynak üretecinin `AgentDefinition`'dan otomatik türettiği şema **değil** | Yeni bir `AgentDefinition` alanı eklendiğinde `TraconCoreJsonContext` yeterli değildir — `Internal/AgentDefinitionPayload.cs`'e de elle eklenmelidir. Ölçüldü: `Compaction`/`Memory` alanları eklenmeden önce PostgreSQL round-trip testi sessizce `null` döndürüyordu (build/test kırmıyordu, yalnızca round-trip testi yakaladı). **Aynı dosyada Faz 12'den kalma bağımsız bir hata daha bulundu ve düzeltildi:** `CallableAgentNames` de bu payload'da hiç yoktu — PostgreSQL'e yazılan bir agent'ın çağırabileceği alt agent listesi sessizce kayboluyordu. |
| `CompactionMessageIndex` gerçek bir `Microsoft.ML.Tokenizers.Tokenizer` ister ama `CompactionProvider`'ın kendisi **tokenizer parametresi almaz** | Ölçüldü: `CompactionProvider(strategy, stateKey, loggerFactory)` ile kurulan bir agent gerçek bir `RunAsync` çağrısında hatasız çalıştı — MAF tokenizer'ı içeride kendisi çözüyor, `Microsoft.ML.Tokenizers.Data.*` gibi ek bir veri paketi **gerekmedi**. Bu, §13.4'ün "yeni paket yok" kararını doğrular. |
| Enum HTTP'ye yansıyorsa `JsonStringEnumConverter` **eklenmeden** unutmak sessiz bir sayı sızıntısı üretir | `CompactionStrategyKind` ilk yazıldığında bu öznitelik unutuldu; bir fonksiyonel test (`.GetString()` beklerken sayı geldi) yakaladı. K-040 deseni her yeni HTTP'ye yansıyan enum için tekrarlanmalı. |

---

## Amaç

MAF'ta hazır olan ve daha önce hiç kullanılmayan bağlam yönetimi yetenekleri açığa çıkarıldı: beş sıkıştırma stratejisi (+ sabit sıralı bir pipeline) ve üç bellek sağlayıcısı (dosya belleği, todo, metin araması), hem düz `ChatClientAgent` hem `HarnessAgent` yolunda. ---

## Bu Fazda Verilen Kararlar (gerçekleşen — K-104'ten devam)

1. **Sıkıştırma `AIContextProviders`/`HarnessAgentOptions.CompactionStrategy` üzerinden bağlanır** (K-053 ile tutarlı).
2. **Tokenizer bağımlılığı zaten geçişli — yeni paket yok** (K-104).
3. **`ChatHistoryMemoryProvider` bu fazın kapsamı dışında** — VectorStore gerekçesiyle (K-105, kullanıcı kararı).
4. **Sıkıştırma varsayılan kapalıdır** (K-106, kullanıcı kararı).
5. **Özetlenen mesajlar `conversation_items`'ta saklanır, silinmez** (K-107, kullanıcı kararı).
6. **Özet modeli çözümleme sırası:** agent ayarı → `TraconOptions.UtilityModel` → agent'ın kendi modeli (K-108).
7. **Özet token'ları çalıştırmanın toplamına dâhildir** (K-108, `CompactionUsageAccumulator`/`MergeUsage` ile).
8. **`Pipeline` sırası sabittir:** ToolResult → SlidingWindow → Summarization (K-109).
9. **`TextSearchProvider` kayıtlı `AgentFileStore`'a bağımlıdır**, bu faz `InMemoryAgentFileStore` (K-110).

Tam gerekçeler `docs/KARARLAR.md`'de.

---

## Bitiş Ölçütleri (DoD)

- [x] Beş stratejinin (+ Pipeline) her biri bir agent tanımından kurulabiliyor
- [x] Uzun konuşmada sıkıştırma tetikleniyor; öncesi/sonrası ölçüm bu dokümanda (yukarıda)
- [x] Özet çağrısının token'ları çalıştırma toplamında görünüyor (`RunRecordingAgentTests.Ozetleme_token_kullanimi_calistirma_toplamina_eklenir`)
- [x] Sıkıştırma sonrası oturum kaydedilip geri yüklenebiliyor (PostgreSQL round-trip testi)
- [x] Geçersiz ayar derlemede hata veriyor, sessizce yok sayılmıyor
- [x] Tokenizer bağımlılığı ölçüldü ve karar yazıldı (K-104)
- [x] Dört doğrulama kapısı sıfır uyarı

---

## Sonraki Faza Devir Notu

- Faz 14'ün `attachments` tablosu kalıcı `AgentFileStore` için doğal ev olabilir.
- `ChatHistoryMemoryProvider` (vektör tabanlı bellek) hâlâ hiç kullanılmıyor —
  bir `VectorStore` implementasyonu ve embedding sağlayıcısı kararı gerektirir;
  ayrı bir faz olarak planlanmalı (F-11'in tamamlanmamış kısmı).
- Faz 18 (eval) sıkıştırmanın kaliteyi düşürüp düşürmediğini ölçebilecek ilk araçtır.
- Faz 20 (maliyet) özet çağrılarını ayrı bir kalem olarak gösterebilir — `RunUsage`
  zaten toplamı taşıyor ama özet payı ayrıştırılmış değil.
- **Bağımsız bulunan hata:** `AgentDefinitionPayload`'da `CallableAgentNames`
  eksikti (Faz 12'den kalma); bu fazda bulunup düzeltildi. Yeni bir
  `AgentDefinition` alanı eklerken **hem** `TraconCoreJsonContext`'in
  kapsadığı tipi **hem** `Tracon.PostgreSql/Internal/AgentDefinitionPayload.cs`'i
  güncellemeyi unutmayın — ikisi ayrı şemalardır.
