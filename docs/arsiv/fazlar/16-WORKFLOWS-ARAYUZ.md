# Faz 16 — Workflows: Graf, Arayüz ve Human-in-the-Loop

> **Durum:** ✅ Tamamlandı (2026-08-03)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-27** (2/2)
> **Önkoşul:** [Faz 15](15-WORKFLOWS-YURUTME.md)
> **Sonraki:** [Faz 17](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) — iş kuyruğu
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.Workflows`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/16-WORKFLOWS-ARAYUZ.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Bu Fazda Ne Yapıldı

Faz 15 workflow'ları çalıştırıyordu ama kullanıcı ne olduğunu göremiyordu ve
insan girdisi isteyen bir graf yarım kalıyordu. Bu faz üçünü kapattı:

1. **Graf görselleştirme** — derlenmiş graf arayüzde elle çizilen SVG olarak
2. **Human-in-the-loop** — bekleyen istek, kontrol noktası, yanıt, sürdürme
3. **Magentic plan onayı** — Faz 15'te kapatılan `RequirePlanSignoff` açıldı

Ayrıca **kontrol noktalarının süreç ömrü sınırı kaldırıldı**: bekleyen bir insan
isteği artık dağıtımda kaybolmaz.

---

## 🚨 Ölçülen MAF Davranışları

Bu bölüm fazın en değerli çıktısıdır. Aşağıdakiler reflection ve **gerçek
çalıştırma** ile ölçüldü; hiçbiri MAF dokümanında yazılı değildir.

### 1. Grafta kimliği değişken olan **tek** şey agent executor'udur

Faz 15 dokümanı "kalıcı kimlik için hazır desenler yeniden yazılmalı" diyordu.
Ölçüldü — **gerek yok**. Beş desenin ürettiği executor kimlikleri:

| Desen | Üretilen executor kimlikleri |
|-------|------------------------------|
| `Sequential` | `{ad}_{AIAgent.Id}` ×N, `OutputMessages` |
| `Concurrent` | `Start`, `{ad}_{AIAgent.Id}` ×N, `Batcher/{ad}_{AIAgent.Id}` ×N, `ConcurrentEnd` |
| `Handoff` | `HandoffStart`, `{ad}_{AIAgent.Id}` ×N, `HandoffEnd` |
| `GroupChat` | `GroupChatHost`, `{ad}_{AIAgent.Id}` ×N |
| `Magentic` | `MagenticOrchestrator`, `{ad}_{AIAgent.Id}` ×N |

Yardımcı dügümlerin tamamı **zaten sabittir**. Yalnızca `AIAgent.Id`
sabitlenirse beş desenin tamamı kalıcı kimlik kazanır ve grafı elle kurmak
gerekmez.

### 2. `AIAgent.Id` yazılabilir bir alanın arkasındadır

```
AIAgent alani: String <Id>k__BackingField (initonly=False)
AIAgent.Id     virtual=False  canWrite=False
```

Özellik sanal değildir ve setter'ı yoktur; ama derleyicinin ürettiği arka alan
salt-okunur **değildir**. `WorkflowAgentIdentity` yalnızca Tracon'in kendi
sarmalayıcı örneğinde bu alanı yazar — MAF'ın hiçbir nesnesine dokunmaz.

### 3. Kontrol noktası bekleyen isteği **taşır** ve istek yeniden yayınlanır

Ölçüldü: bir `RequestInfoEvent` sonrası yazılan kontrol noktasından **yeni bir
`Workflow` örneğiyle** sürdürüldüğünde MAF aynı istegi **aynı `RequestId` ile
yeniden yayınlar**. Human-in-the-loop bu davranışın üzerine kuruldu: yanıt
saklanan bir nesneyle değil, yeniden yayınlanan istekle eşleştirilir.

### 4. 🚨 Yanıt gönderildikten sonra akış **yeniden açılmalıdır**

```csharp
await run.SendResponseAsync(response);   // mesaji kuyruklar
// ...ama o sirada tuketilen WatchStreamAsync numaralandiricisi
// zaten bitmeye karar vermistir.
```

`SendResponseAsync` bir mesaj kuyruklar, ancak o anda tüketilen
`WatchStreamAsync` numaralandırıcısı akışı bitirir. Devam eden super-step'leri
yalnızca **yeni bir numaralandırıcı** görür. `WorkflowRunner.PumpAsync` bu
yüzden bir dış döngü taşır: yanıt gönderildiyse akış yeniden açılır.

### 5. 🚨 Çıktı tipi **işleyicinin dönüş tipinden** bildirilir

Gövdesinde `YieldOutputAsync` çağıran, dönüşü olmayan bir işleyici hiçbir çıktı
tipi beyan etmez ve çalışma anında düşer:

```
Cannot output object of type String. Expecting one of [].
```

Doğrusu `BindAsExecutor<TInput, TOutput>` ile dönüş tipi olan bir işleyici
yazmak, ayrıca `WithOutputFrom(...)` çağırmaktır.

### 6. 🚨 `ForwardIncomingMessages` açıkken sonraki düğüm **iki kez** çalışır

Ölçüldü (örnek uygulama, gerçek model): agent host'u hem gelen mesajı hem kendi
yanıtını aşağı yollar; port iki kez tetiklenir ve tek bir onay yerine **iki ayrı
bekleyen istek** oluşur. `/requests` iki kayıt döndürdü. Çözüm:

```csharp
new AIAgentBinding(agent, new AIAgentHostOptions { ForwardIncomingMessages = false })
```

### 7. Elle kurulmuş grafta sürdürme, kontrol noktasında **kuyrukta kalan** işi tekrarlar

Ölçüldü: `ozetle-ve-onayla` sürdürüldüğünde özetleyici agent yeniden çalıştı
(37 `MessageDelta`) ve graf ikinci bir onay istedi. Sebep Tracon değil,
grafın şeklidir — kontrol noktası anında agent host'u için kuyrukta bir mesaj
duruyordu. Hazır desenlerde ve Magentic plan onayında bu görülmez: orkestratör
turu kendi elinde tutar.

---

## HTTP Uçları (Faz 16'da eklenenler)

| Uç | Rol | Ne yapar |
|----|-----|----------|
| `GET {prefix}/api/workflows/{name}/graph` | Reader | Derlenmiş graf + Mermaid metni |
| `GET {prefix}/api/workflows/runs/{runId}/requests` | Reader | Bekleyen istekler (yalnız `AwaitingInput`) |
| `POST {prefix}/api/workflows/runs/{runId}/respond` | Operator | Yanıtlar ve SSE ile sürdürür |

`501 Not Implemented` — motor kayıtlı değilse `/graph`, `/requests` ve
`/respond` uçlarında. Graf **derlenmiş** workflow'dan çıkarılır; derleyici
motorla gelir.

---

## Bitiş Ölçütleri (DoD)

- [x] Workflow grafı arayüzde çiziliyor; düğümler çalıştırma sırasında renkleniyor —
      `foldNodeStates` olayları kimlik üzerinden eşler; E2E `Workflow_grafi_cizilir`
- [x] Mermaid metni kopyalanabiliyor — E2E `Mermaid_metni_kopyalanabilir`, panodan
      `flowchart` doğrulandı
- [x] İnsan girdisi isteyen workflow bekliyor, cevaplanıyor ve tamamlanıyor —
      gerçek model + PostgreSQL: `WorkflowOutput: "Ozet yayinlandi."`
- [x] **Süreç yeniden başladığında sürdürme kararı verildi ve UYGULANDI** —
      kimlik yeniden başlatma sonrası aynı kaldı, yeniden başlatmadan önce oluşan
      istek sonrasında cevaplandı ve çıktı üretildi (bkz. 16.1 kanıtı)
- [x] `GroupChat` ve `Magentic` desenleri çalışıyor — Faz 15'te tamamlandı
- [x] `Magentic` plan onayı açılabiliyor — gerçek modelle plan metni alındı,
      onaylandı, katılımcı agent çalıştı
- [x] Declarative kararı verildi ve gerekçesi yazıldı — K-129, iki ölçümle
- [~] Bundle **+12,8 KB gzip** arttı (105,2 / 250 KB) — plan +10 KB öngörüyordu;
      kapsam kullanıcı kararıyla tanım editörünü de kapsadı
- [x] Dört doğrulama kapısı sıfır uyarı; `dotnet pack` 9 paket üretiyor

---

## Uygulama Sırasında Yaşanan Hatalar

Bu bölüm sonraki fazın en değerli bilgisidir.

### 1. Çıktı üretmeyen executor sessizce "tamamlandı" oldu

**Belirti:** İnsan yanıtı gönderildi, yürütme devam etti, ama hiçbir çıktı
gelmedi ve çalıştırma `Completed` kaydedildi.

**Kök neden:** İki ayrı sorun. (a) `YieldOutputAsync` çağıran ama dönüşü olmayan
işleyici hiçbir çıktı tipi beyan etmiyordu → `Cannot output object of type
String. Expecting one of []`. (b) `WorkflowErrorEvent` olay akışına yazılıyor ama
çalıştırma durumunu değiştirmiyordu.

**Çözüm:** (a) dönüş tipli işleyici + `WithOutputFrom`. (b) graf hatası artık
çalıştırmayı `Failed` yapar.

### 2. Graf `Concurrent` deseninde iki kat agent gösterdi

**Belirti:** İki agent'lı bir `Concurrent` workflow'un grafında dört agent
düğümü çıktı.

**Kök neden:** Agent adı, kimlikten `{ad}_{32 onaltilik}` kalıbıyla
ayrıştırılıyordu; `Batcher/bir_<32hex>` de bu kalıba uyuyordu.

**Çözüm:** Eğik çizgi taşıyan bileşik kimlikler ayrıştırılmaz. Regresyon testi
`Concurrent_grafi_dagitici_ve_birlestirici_dugumleri_gosterir`.

### 3. Bir onay yerine iki bekleyen istek oluştu

**Belirti:** Örnek uygulamada `/requests` iki kayıt döndürdü.

**Kök neden:** `AIAgentHostOptions.ForwardIncomingMessages` açıkken agent host'u
hem gelen mesajı hem yanıtını aşağı yolluyor, sonraki düğüm iki kez çalışıyordu.

**Çözüm:** Örnekte `ForwardIncomingMessages = false`. **Yalnızca örnek
uygulamayı gerçekten çalıştırmak ortaya çıkardı** — 799 test yeşildi.

### 4. Kodda tanımlı workflow'un `kind` alanı arayüzü kırıyordu

**Belirti:** Katalogda desen rozeti boş çıkıyordu.

**Kök neden:** `WorkflowDescriptor.Kind` kod workflow'larında `null`'dur; arayüz
tipi bunu zorunlu varsaymıştı.

**Çözüm:** Tip düzeltildi, "code graph" rozeti eklendi. Yine yalnızca gerçek
çalıştırma yakaladı.

### 5. Playwright gizli `<option>` metnini buldu

**Belirti:** Runs listesindeki `awaiting input` rozetini bekleyen test, durum
süzgecindeki gizli `<option>Awaiting input</option>` öğesini bulup zaman aşımına
uğradı.

**Çözüm:** `Exact = true`. Faz 8'in `GetByPlaceholder` tuzağının aynısı.

---

## Sonraki Faza Devir Notu

- **İş kuyruğu `AwaitingInput` durumunu hesaba katmalıdır** (Faz 17): bekleyen
  bir çalıştırma ne çalışıyor ne bitmiş; zamanlanmış bir tetikleyici onu yeniden
  başlatmamalıdır.
- **Bekleyen istek bildirimi yoktur.** Kimse arayüze bakmıyorsa istek görülmez.
  Faz 21'in webhook'u bunu `RunAwaitingInput` olayı üzerinden çözebilir.
- **Bekleyen çalıştırmalar birikir.** Zaman aşımı yalnızca akış açıkken
  çalışır; `AwaitingInput` bir çalıştırma süresiz bekler. Faz 25'in saklama
  politikası bunu ele almalıdır.
- **Faz 20 (maliyet):** plan onayı açık bir Magentic workflow'u her turda
  yönetici agent'ı çalıştırır; maliyet raporu bunu ayrı gösterebilmelidir.
- **Declarative yeniden açılabilir** (K-129): MAF `ChatClientAgent` tabanlı bir
  sağlayıcıyı desteklerse veya hazır bir `ResponseAgentProvider` yayınlarsa.
