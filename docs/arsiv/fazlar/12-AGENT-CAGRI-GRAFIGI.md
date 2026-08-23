# Faz 12 — Agent'ın Agent'ı Çağırması

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-10**
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0005

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/12-AGENT-CAGRI-GRAFIGI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir agent, kataloğdaki başka bir agent'ı çağırabilsin. MAF'ta hazırdır ve Faz 6'da **bilerek kapalı bırakılmıştı** (K-062): kaynak sınırı, denetim izi ve özyineleme koruması tasarlanmadan açılması doğru olmazdı. Bu faz o tasarımı yaptı ve özelliği açtı. ---

## Cevaplanmış Tasarım Soruları

| Soru | Cevap | Karar |
|------|-------|-------|
| Özyineleme nasıl kesilir? | **İki katman**: kaydetme anında statik döngü denetimi + çalışma anında derinlik sayacı | — |
| Alt çalıştırma ayrı `runs` satırı mı? | **Evet** | K-093 |
| Kiracı? | **Aynı kiracı, istisnasız** | — |
| Token bütçesi? | **Ağaç boyunca paylaşılan tek nesne** | K-096 |
| Onay kime sorulur? | **v1: alt agent onay isteyemez**; isteyen alt çalıştırma `Failed` olur | K-103 |
| Varsayılan `MaxDepth`? | **3** *(kullanıcı kararı)* | K-101 |
| Varsayılan token bütçesi? | **200.000 / ağaç** *(kullanıcı kararı)* | K-101 |
| Alt çalıştırma SSE'de görünsün mü? | **Özet olay** *(kullanıcı kararı)* | K-102 |
| `OnlyRootRuns` varsayılanı? | **`true`** *(kullanıcı kararı)* | K-100 |

---

## Gerçek Model Kanıtı

`samples/AgentPrism.Api`, `gpt-5.4-mini`, `yonlendirici → support`:

```
$ curl -sN -X POST .../api/agents/yonlendirici/run \
       -d '{"message":"ORD-7 durumunu ogren ve ozetle"}'

=== KOK LISTESI (varsayilan) ===
yonlendirici   depth=0 children=1 tokens=904 tree=1502

=== TUM (includeChildren=true) ===
support        depth=1 parent=019fc370 root=019fc370 tokens=598 status=Completed
yonlendirici   depth=0 parent=None     root=None     tokens=904 status=Completed

=== KOK OLAY OZETI ===
   1 child.completed      1 child.started       40 message.delta
   1 run.completed        1 run.started          4 tool.invoked / 4 tool.invoking

=== SPAN AGACI (kok calistirmanin /trace ucu, 16 span) ===
agentprism.run
  invoke_agent yonlendirici(yonlendirici)
    chat gpt-5.4-mini
    execute_tool background_agents_start_task
      agentprism.run                          <-- ALT CALISTIRMA, IC ICE
        invoke_agent support(support)
          chat gpt-5.4-mini
          execute_tool get_order_status
          chat gpt-5.4-mini
    chat gpt-5.4-mini
    execute_tool background_agents_wait_for_first_completion
    chat gpt-5.4-mini
    execute_tool background_agents_get_task_results
    chat gpt-5.4-mini
    execute_tool background_agents_clear_completed_task
    chat gpt-5.4-mini

=== ALT CALISTIRMANIN /trace UCU ===
HTTP 404   (beklenen: trace'in sahibi koktur — K-099)
```

Derinlik sınırı gerçek yapılandırmayla da doğrulandı:

```
$ AgentPrism__AgentGraph__MaxDepth=0 dotnet run ...
"'support' agent'i cagirilamadi: cagri derinligi siniri asildi
 (izin verilen en fazla derinlik 0). Isi kendin tamamla veya
 daha az katmanli bir cagri zinciri kur."

calistirma sayisi: 1  [('yonlendirici', 0)]   # alt satir HIC olusmadi
```

---

## Plandan Sapmalar

| # | Sapma | Gerekçe |
|---|-------|---------|
| S1 | `RunRecord`'a planda olmayan `ChildRunCount` ve `TreeUsage` eklendi | §12.5 "3 alt çalıştırma rozeti" ve "ağaç maliyeti ayrı sütun" gereksinimleri bu iki alan olmadan karşılanamıyordu. İkisi de okumada `LATERAL` alt sorguyla hesaplanır, saklanmaz. |
| S2 | `AgentPrismRunOptions`'a planda olmayan `RootRunId` eklendi | Plan `root_run_id` sütununu öngörüyordu ama değerin ağaç boyunca **nasıl taşınacağını** yazmamıştı. Ayarlarda taşınmasa her alt çalıştırma kökü kendisi sanardı. |
| S3 | Alt agent **geç** çözülür; derleme anında değil | Plan "her ad için `IAgentCatalog.ResolveAsync`" diyordu. Derleme anında çözmek DI dairesi kurardı ve alt agent güncellendiğinde çağıranın önbelleği bayatlardı (K-098). |
| S4 | `AgentPrismRunContext.SetCurrentRunId` **kaldırıldı** | Kimlik tek başına yetmiyordu; kapsam derinlik, bütçe, kiracı ve olay yazıcısını da taşımak zorunda. Faz 11'in tek çağıranı (`SandboxedSkillScriptRunner`) `CurrentRunId` özelliğini okumaya devam ediyor, değişiklik gerekmedi. |
| S5 | Trace sahipliği kısıtı (K-099) planda yoktu | Gerçek çağrıda ortaya çıktı: kökün `/trace` ucu 404 dönüyordu. Yalnız birim testleriyle yakalanamazdı. |
| S6 | Akışlı yolda `AsyncLocal` yeniden yazımı planda yoktu | Ölçüldü; §12.2'de belgelendi. Faz 11'in skill script kimliğini de onardı. |
| S7 | Örnek uygulamada yönlendirici `arastirmaci`'yı değil `support`'u çağırıyor | `arastirmaci` harness kullanır ve K-053'te belgelenen harness kusuru alt çalıştırmayı da vururdu. Örneğin çalışır olması, mimariyi anlatmasından önce gelir. |
| S8 | Faz 11'den kalan 276 `IDE0055` biçim hatası düzeltildi | `main` üzerinde `dotnet build` kırmızıydı; iki dosya (`ISkillScriptGrantStore.cs`, `SkillScriptGrant.cs` ve türevleri) üç boşluk girinti taşıyordu. Bu fazın kapıları yeşile ancak düzeltildikten sonra dönebildi. |

---

## Bitiş Ölçütleri (DoD)

| Ölçüt | Durum |
|-------|-------|
| Bir agent başka bir agent'ı çağırıyor; iki ayrı `runs` satırı oluşuyor | ✅ Gerçek modelle doğrulandı: `yonlendirici` 904 token, `support` 598 token, ayrı satırlar |
| Waterfall'da alt çalıştırma iç içe görünüyor | ✅ 16 span, `agentprism.run` → `execute_tool background_agents_start_task` → `agentprism.run` |
| Döngülü tanım kaydedilemiyor | ✅ `400 Bad Request`, hata mesajı yolu yazıyor (`a -> b -> c -> a`) |
| Derinlik sınırı çalışma anında da tutuyor | ✅ `MaxDepth=0` ile gerçek çalıştırmada alt satır hiç oluşmadı |
| Bütçe aşımında yeni alt çağrı başlamıyor, hata anlaşılır | ✅ `DescribeExhaustion()` hangi sınırın dolduğunu sayıyla yazıyor |
| Alt agent kiracı değiştiremiyor | ✅ `Kiraci_degistiyse_cagri_reddedilir` |
| Runs ekranı varsayılan olarak yalnız kök çalıştırmaları gösteriyor | ✅ E2E'de doğrulandı; `includeChildren=true` ile eski davranış |
| Dört doğrulama kapısı sıfır uyarı | ✅ build / test (583) / pack / format |

---

## Sonraki Faza Devir Notu

**Sıradaki faz: 13** — [`13-BAGLAM-SIKISTIRMA-VE-BELLEK.md`](13-BAGLAM-SIKISTIRMA-VE-BELLEK.md)

Faz 13'ü etkileyen noktalar:

- **`AIContextProviders` artık iki sağlayıcı taşıyabiliyor.**
  `AgentDefinitionCompiler.CompileChatAgent` bir `List<AIContextProvider>` kurar
  (skill sağlayıcısı + arka plan agent sağlayıcısı). Faz 13'ün
  `CompactionProvider`'ı aynı listeye eklenecektir; kurulum yeri hazırdır.
- **🚨 Akışlı yolda `AsyncLocal` kuralı.** Faz 13 bağlam sıkıştırmasını çalıştırma
  yolunun içinde tetikleyecekse, kapsamı `MoveNextAsync`'ten hemen önce yazma
  kuralı (§12.2) aynen geçerlidir.
- **Önbellek anahtarı artık iki parmak izi taşıyor.**
  `CompiledAgentCache.CombineFingerprints` ile birleştirilir. Faz 13 üçüncü bir
  bağımlılık (sıkıştırma ayarı) eklerse aynı yardımcıyı zincirlemelidir.
- **`AgentRunScope.Writer` kök akışa olay yazmanın tek yoludur.** Faz 13
  sıkıştırma olayı yazmak isterse aynı kanalı kullanmalıdır; ikinci bir
  `RunEventWriter` sıra numaralarını çakıştırır (K-014).

Diğer fazlar:

- **Faz 15 (workflows)** benzer bir çok-agent modeli getirir ama **farklı bir
  yürütme motorudur**. İkisi karıştırılmamalıdır: burada agent bir tool gibi
  çağrılır; orada bir graf yürütülür.
- **Faz 20 (maliyet)** `root_run_id` üzerinden ağaç maliyetini raporlayacaktır;
  `RunRecord.TreeUsage` zaten bu şekli veriyor, fiyat çarpanı eksiktir.
- **Faz 21 (kota)** `AgentRunBudget` ile aynı sayaçları kullanabilir; kiracı
  kotası ile çalıştırma bütçesi **ayrı** kavramlardır, birleştirilmemelidir (K-101).
- **Faz 25 (saklama)** `runs` tablosunun ağaç başına birden çok satır aldığını
  hesaba katmalıdır. `parent_run_id` yabancı anahtar **taşımaz** (K-095), bu
  yüzden toplu silme sıralama kısıtı üretmez.

---

## Riskler — Gerçekleşen Durum

| Risk | Sonuç |
|------|-------|
| Maliyet çarpan etkisi | Paylaşılan bütçe + derinlik sınırı + varsayılan token sınırı ile kapatıldı (K-101) |
| `run_events` hacmi katlanır | Alt çalıştırmalar kendi satırlarına yazar; kök akışa yalnız iki özet olay eklenir (K-102) |
| Span ağacı düzleşir | **Gerçekleşmedi** ama farklı bir sorun çıktı: trace sahipliği çakışması (K-099). Span ağacı gerçek çağrıda iç içe doğrulandı |
| Onay akışı beklenmedik yerde biter | v1'de alt agent onay isteyemez; alt çalıştırma `Failed` olur ve mesaj sebebi yazar (K-103) |
| Kod tarafı fabrika agent'ları statik denetimden kaçar | Çalışma anı derinlik sayacı ikinci savunma hattı olarak çalışıyor |
