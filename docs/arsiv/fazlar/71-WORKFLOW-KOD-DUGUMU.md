# Faz 71 — Workflow Kod Düğümü

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-116**
> **Önkoşul:** [Faz 15](15-WORKFLOWS-YURUTME.md) — workflow yürütme ve kalıcılık · [Faz 16](16-WORKFLOWS-ARAYUZ.md) — graf, arayüz, human-in-the-loop
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Workflows`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok (düğüm tanımı var olan workflow tanımında yaşar) · **Doğrulanacak:** tanım sütununun şeması değişiyorsa üç set gerekir
> **Public API:** **büyüyor** — `WorkflowNodeKind` enum'una **ekleme**, `WorkflowDefinition`'a alan, bir kayıt yüzeyi. `PublicAPI.Shipped.txt` bugün **boş** — şimdi bedava
> **Site etkisi:** `concepts/workflows.md` (`guides/background-work.md` PLANDA
> vardı ama dokunulmadı — bkz. Plandan Sapmalar #5, ilgisiz çıktı)
> **Manuel test alanı:** [`docs/manuel-test/15-WORKFLOWS.md`](../../manuel-test/15-WORKFLOWS.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/71-WORKFLOW-KOD-DUGUMU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism Workflows bugün yalnız **agent zinciri** kurabiliyor. Gerçek bir üretim hattında ise AI çağırmayan adımlar vardır: dosya indirme, biçim dönüştürme, ses sentezi, veritabanı yazımı. Bunlar grafiğe giremediği için tüketici workflow'u yalnız hattının AI kısmı için kullanabiliyor; kalanını kendi kuyruğunda tutuyor.

## Bitiş Ölçütleri (DoD)

- [ ] Kod düğümü kaydedilmemişken hiçbir davranış değişmez
- [ ] Agent → fonksiyon → agent zinciri uçtan uca koşar; çıktı belgeye yazıldı
- [ ] Kayıtlı olmayan ada işaret eden tanım **kaydetme anında** reddedilir
- [ ] Fonksiyon düğümü `ExecutorInvoked`/`ExecutorCompleted`/`ExecutorFailed` üretir
- [ ] İptal fonksiyona ulaşır
- [ ] Kontrol noktasından devam davranışı **ölçüldü ve belgelendi**
      (idempotency sözleşmesi dokümana yazıldı)
- [ ] Kod düğümü maliyet toplamına `0` katkı verir
- [x] Bilinmeyen düğüm tipi eski istemcide yok sayılır — 🚨 bağımsız denetimde
      BULUNDU ve kapandı: `WorkflowGraphView`'in `KIND_STYLE[node.kind]`
      araması tanımadığı bir `kind` için `undefined` döndürüyordu ve
      `style.stroke` erişimi TypeError ile ÇÖKERDİ — "yok sayma" iddiası
      doğru değildi (`WorkflowNodeKind`'a her yeni değer eklendiğinde var
      olan, hiç kapanmamış bir kırılganlık, Faz 16'dan beri). Çözüm:
      `KIND_STYLE[node.kind] ?? KIND_STYLE.Unknown` — tanımadığı her `kind`
      artık `Unknown`'ın stiline düşer, çökmez
      (`src/AgentPrism.UI/frontend/src/components/workflow-graph.tsx`).
      .NET tarafı için iddia geçerli DEĞİLDİR: `WorkflowNodeKind` düz
      `JsonStringEnumConverter<T>` kullanır ve tanımadığı bir adı
      **fırlatarak** reddeder — bu yalnız arayüz (TypeScript, çalışma-anında
      tip denetimi olmayan) tarafı için bir gereklilikti.
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek workflow koşumu yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri [`docs/manuel-test/15-WORKFLOWS.md`](../../manuel-test/15-WORKFLOWS.md)
      içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Kayıtlı kod düğümleri
curl -s http://localhost:5081/agentprism/api/workflows/functions | jq

# Graf yeni tipi taşıyor mu
curl -s http://localhost:5081/agentprism/api/workflows/mixed/graph | jq '.nodes[].kind'
```

---

## Plandan Sapmalar

1. **Kod düğümü desteği yalnız `Sequential` içinde uygulandı, plan kapsamıyla
   birebir** — sapma değil, plan böyle öngörmüştü. §71.2'nin mermaid şeması
   basitleştirilmiş bir akış çiziyordu; gerçek uygulama iki katman gerektirdi
   (bkz. K-494, K-495) ve şema plandaki kadar sade değil çıktı.
2. **Agent düğümü `AIAgentBinding` değil `WorkflowAgentStepExecutor` olarak
   bağlanıyor** — planın §71.2 mermaid'i "WorkflowRunner düğümü FunctionExecutor
   olarak bağlar" derken yalnız FONKSİYON düğümünü kastediyordu; agent
   düğümünün de bir `FunctionExecutor` alt sınıfı olması **ölçülerek**
   ortaya çıktı (K-495 — `AIAgentBinding` giriş dışı düğümde çalışmıyor).
   Yan etki: karışık zincirdeki bir agent adımı workflow'un üst seviye olay
   akışına `MessageDelta` yaymıyor (agent'ın kendi çocuk `runs` satırı yine
   de tam geçmiş tutuyor). Plan bu ayrıntıyı öngörmemişti çünkü MAF'ın
   agent-host protokolünün giriş-dışı düğümde çalışmadığı önceden bilinmiyordu.
3. **`WorkflowGraphReader.Classify`'a Function tanıma eklenirken agent-node
   sınıflandırması geçici olarak bozuldu, bağımsız denetimden ÖNCE
   kendi testimle yakalandı ve düzeltildi** — ilk tasarımda hem agent hem
   fonksiyon düğümü `FunctionExecutor`-türetilmiş olduğu için ikisi de
   `Function` olarak çiziliyordu (manuel doğrulama sırasında, gerçek
   `samples/AgentPrism.Api` koşumunda görüldü). Çözüm `WorkflowAgentStepExecutor`
   tip adını `AgentNameOf`'a tanıtmaktı; bkz. K-495.
4. **Fonksiyon zaman aşımı (Açık Soru 2) çözülmedi** — plan zaten bunu
   "Kapsam dışı" işaretlemişti; K-497 bu durumu resmileştirdi.
5. **`guides/background-work.md` güncellenmedi** — plan başlığın "Site
   etkisi" alanında bu sayfayı listelemişti, ama inceleme gösterdi ki sayfa
   TAMAMEN farklı bir arka plan mekanizmasından (zamanlanmış iş kuyruğu)
   bahsediyor; kod düğümüyle doğal, zorlamasız bir bağlantı yok. Plan
   tahmini yanlış çıktı — `concepts/workflows.md` güncellendi, bu sayfa
   dokunulmadan bırakıldı.
6. **UI'nin workflow editör ekranı fonksiyon seçici KAZANMADI** — plan
   "Arayüz payı" bölümünde "yeni bir düğüm şekli **ve** fonksiyon seçici"
   sözü veriyordu; yalnız GRAF GÖRÜNÜMÜ tarafı (şekil, renk, lejant, `Nodes`/
   `WorkflowFunctionResponse` tipleri) teslim edildi. DoD'nin kendisi
   yalnız "fonksiyon düğümü agent düğümünden ayırt edilebilir çizilir"
   diyordu (Manuel Case 8) — bu karşılandı. Editördeki YAZMA tarafı (bir
   `WorkflowNodeReference` listesi kurma arayüzü) kapsam/efor dengesiyle
   bilinçli olarak bu fazın dışında bırakıldı; karışık düğümlü bir workflow
   bugün yalnız `PUT /api/workflows/{name}` ile (doğrudan HTTP çağrısı)
   oluşturulabilir. Bağımsız denetimde bulunan yanlış bir kod yorumu
   (editörün bunu desteklediğini iddia eden) düzeltildi;
   `samples/AgentPrism.Api/Program.cs`'teki not artık bu boşluğu açıkça
   söylüyor. **Sonraki faz için aday**, `docs/ADAYLAR.md`'ye eklenmeli.
7. **`ChatForwardingExecutor` ve elle `TurnToken` gönderme denendi, ikisi de
   terk edildi** — plan bu ayrıntı düzeyine inmemişti (§71.2 mermaid'i tek
   bir "WorkflowRunner → FunctionExecutor" oku çiziyordu). K-495'in kendi
   metni bu iki başarısız denemeyi kanıt olarak taşıyor; sonraki bir
   oturumun aynı yolu yeniden denememesi için.

## Bu Fazda Verilen Kararlar

- **K-494** — Fonksiyon düğümü yalnız `Sequential`'da desteklenir;
  `WorkflowDefinition.Nodes` `AgentNames` ile karşılıklı dışlanır.
- **K-495** — Karışık zincirde agent düğümü `AIAgentBinding` değil
  `WorkflowAgentStepExecutor` (bir `FunctionExecutor` alt sınıfı) olarak
  bağlanır — `AIAgentBinding` giriş dışı düğümde çalışmıyor, ÖLÇÜLDÜ.
- **K-496** — Fonksiyon adı kaydetme anında da doğrulanır (agent adının
  aksine, yalnız derleme anında); kayıt süreç ömrü boyunca sabittir.
- **K-497** — Kod düğümünün kendi zaman aşımı bu fazda ele alınmadı; Faz
  69'un tool timeout sözleşmesi tek aday olarak bırakıldı (Açık Soru 2,
  ÇÖZÜLMEDİ).
- **K-498** — Kontrol noktasından devam sözleşmesi ÖLÇÜLDÜ: en son kontrol
  noktasından sürdürme kod düğümünü yeniden çağırmaz, daha erken bir kontrol
  noktasından sürdürme çağırır — `AddWorkflowFunction` işleyicisi bu yüzden
  idempotent olmak zorundadır.

Tam metin: [`KARARLAR.md`](../../KARARLAR.md), K-494 – K-498.

## Denetim Bulguları

Bağımsız denetim `general-purpose` alt-agent ile taze bağlamda koşuldu
(2026-08-19). Sonuç: **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `samples/AgentPrism.Api/Program.cs`'teki yorum, karışık düğümlü bir workflow'un UI'nin workflow editöründen de oluşturulabildiğini YANLIŞ iddia ediyordu — editör hiç güncellenmedi. | **Düzeltildi.** Yorum artık editörün fonksiyon seçici taşımadığını açıkça söylüyor; bkz. Plandan Sapmalar #6. |
| 2 | 🟡 | `AddWorkflowFunction`'ın işleyicisi thread-safe olmak ZORUNDA (tek kayıt, paylaşılan kapanış) ama bu hiçbir yerde yazılı değildi — planın kendi "Beş soru" listesi bunu açıkça istiyordu. | **Düzeltildi.** XML belgesine ve `docs-site/concepts/workflows.md`'ye eklendi. |
| 3 | 🟡 | DoD satırı "Bilinmeyen düğüm tipi eski istemcide yok sayılır" hiçbir zaman doğru değildi: `KIND_STYLE[node.kind]` tanımadığı bir `kind` için `undefined` döner, `style.stroke` erişimi TypeError ile ÇÖKER — Faz 16'dan beri var olan, hiç kapanmamış bir kırılganlık. | **Düzeltildi.** `KIND_STYLE[node.kind] ?? KIND_STYLE.Unknown` düşümü eklendi (`workflow-graph.tsx`); DoD satırı gerçekleşen davranışı yansıtacak şekilde güncellendi. |
| 4 | 🟢 | `WorkflowGraphReader.AgentNameOf`'un hex-suffix sezgiseli, adı tesadüfen `{ad}_{32-hex}` biçimine denk gelen bir fonksiyon düğümünü yanlışlıkla `Agent` sınıflandırabilir. | **Gerekçelendi, aday eklenmedi.** Aşırı uç durum; `Concurrent` deseninin `Batcher` düğümleri için Faz 16'dan beri kabul edilen AYNI sınıf kısıtlama — Faz 71 bunu kötüleştirmiyor. |

**Temiz çıkan başlıklar:** 3.1 (DoD), 3.2 (test tiyatrosu), 3.3 (test seviyesi),
3.5 (imza-gövde), 3.6 (plan dışı public API), 3.7 (repo kuralları) — denetçinin
tam raporu bu oturumun geçmişindedir, özet burada tutulur.

Denetimden sonra dört kapı yeniden koşuldu (bkz. Doğrulama komutları altı);
hepsi yeşil.

## Sonraki Faza Devir Notu

- **Workflow editörü fonksiyon seçici KAZANMADI** (Plandan Sapmalar #6).
  Karışık düğümlü bir workflow bugün yalnız HTTP API'den (`PUT
  /api/workflows/{name}`) kurulabilir. Bir sonraki oturum bunu `docs/ADAYLAR.md`'ye
  aday olarak eklemeli; kapsam en az bir "düğüm listesi kurucu" (ekle/sil/
  sırala, Agent↔Function seçici) ve `GET /api/workflows/functions`'a bağlı
  bir fonksiyon seçici gerektirir.
- **Kod düğümü zaman aşımı hâlâ yok** (K-497, Açık Soru 2). Faz 69'un
  `TimeoutAIFunction` deseni yeniden kullanılabilir aday olarak duruyor.
- **Karışık zincirdeki agent adımı `MessageDelta` yaymaz** (K-495'in yan
  etkisi). Bir tüketici canlı token akışını KARIŞIK zincirlerde beklerse
  bu bir sürprizdir — belgelendi ama giderilmedi. Gerçek bir ihtiyaç
  ölçülürse, `WorkflowAgentStepExecutor`'ın kendi işleyicisinden
  `IWorkflowContext` üzerinden akış olayı yaymanın bir yolu araştırılmalı.
- **Alt agent çağırma ve Concurrent/Handoff/GroupChat/Magentic'e fonksiyon
  düğümü ekleme** plan tarafından zaten kapsam dışı bırakılmıştı (§71.2
  "Kapsam dışı" tablosu); bu faz bu sınırı değiştirmedi.
