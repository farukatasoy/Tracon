# Faz 250 — Alt Agent Sürüm Sabiti

> **Durum:** 📋 Planlandı (2026-10-26)
> **Kademe:** tam — public API'den satır silinir (`AgentDefinition.CallableAgentNames` yerine `CallableAgents`) ve kalıcı veri şekli değişir (saklanan tanım JSON'u eski alanı okumaya devam etmeli); HTTP sözleşmesi, iki üretilen istemci ve konsol değişir
> **Plan onayı:** Bakımcı, 2026-10-26 (sohbette alınan kararlar: yapılı liste `CallableAgents: [{name, version?}]` — yan sözlük reddedildi; konsol editörü sabiti gösterir ve düzenletir)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-409**
> **Genellik:** Bir veritabanı agent'ına başka agent'ları çağırtan ve sürüm sabiti kullanan her kurulum: sabitlenmiş bir üst agent (K-989 düğüm sabiti, K-895 onay devamı, deney kolu) bugün çocuklarını güncel sürümle koşturur. İş tüketici kodunda kalamaz: çocuk çağrısı Tracon'un derleyicisinde kurulur. İkinci kanıt ilk ilkedir: sabitlenen sürüm çağırdığını da sabitlemelidir — yoksa "v1 ile koş" sözü yalnız üst agent'ın talimatını kapsar
> **Karşı görüş:** "Çocuğun güncel sürümle koşması bilinçlidir; sürümü karşılaştırmak isteyen çocuğa doğrudan deney kurar" (site `concepts/agents.md:452-460`, K-895 (c)). Cevap: varsayılan değişmez — sabitsiz çocuk güncel sürümü koşar. Sabit yalnız tanım yazarı **açıkça** bir sürüm yazınca uygulanır; deney kolu yine yalnız kurulduğu agent'a uygulanır
> **Önkoşul:** [Faz 248](248-AGENT-SURUM-NUMARASI-KIMLIGI.md) — numara silmede yeniden kullanılırsa çocuk sabiti başka içeriği koşturur
> **Paketler:** `Tracon.Abstractions` (`AgentDefinition`, yeni `CallableAgentReference`), `Tracon.Core` (derleyici, doğrulayıcı, `CallableAgentResolver`, `SubAgentToolNames`, replay, devam), `Tracon.Sql.Shared` (`AgentDefinitionPayload`), `Tracon.AspNetCore` (`AgentContracts`, `AgentEndpoints`, OpenAPI), `Tracon.UI` (agent editörü), `Tracon.Client` + `packages/tracon-client` (üretilir), `Tracon.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok — tanım `jsonb`/JSON metnidir; eski alan okuma anında çevrilir (250.2)
> **Public API:** Değişir — `AgentDefinition.CallableAgentNames` (2 satır) silinir, `CallableAgents` ve `CallableAgentReference` eklenir. `rc.1` öncesi kırıcı değişiklik serbesttir (UR-012 B); sürüm notunda adıyla anılır. `wc -l src/*/PublicAPI.Shipped.txt`: 18 dosya × 1 satır (2026-10-26). `AgentDescriptor.CallableAgentNames` kalır (katalog izdüşümü; çağrı grafı adla kurulur, `AgentCallGraph.cs:83`)
> **Tüketici yüzeyi:** site `concepts/agents.md` (`:452-460` metni tersine döner: "unless the parent pins it"), `concepts/workflows.md` § *Pin an agent step to a version*, üretilen `http-api/schema-agentdefinition*.md`, `ui.md` (editör) · sevk edilen: XML + `<example>`, `PublicAPI.Unshipped.txt`, OpenAPI → iki istemci, `samples/Tracon.Samples.CustomAgentSource`, `CHANGELOG.md` `[Unreleased]` → *Changed* (kırıcı: alan adı) ve *Added*
> **Manuel test alanı:** `docs/manuel-test/29-AGENT-DESTEGI.md` (çağrılabilir agent) · `docs/manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md` (editör)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman ve [Faz 248](248-AGENT-SURUM-NUMARASI-KIMLIGI.md)'in kapanış devri (kapandıysa `FAZ-DEFTERI.md` satırı)
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-895 —\|K-989 —\|K-811 —\|K-1043 —\|K-228 —\|K-232 —" docs/KARARLAR.md
   ```
   **K-895** ((c) alt agent güncel sürümü çözer — bu faz onu tanım sabitiyle
   değiştirir; satıra not düşülür), **K-989** (düğüm sabiti; sabitlenen agent'ın
   kendi alt agent'ları güncel sürümle koşar — bu faz o cümleyi kapatır),
   **K-811** (derleme önbelleği içerik özetiyle; çocuk açıklaması özetin parçası),
   **K-1043** (alt agent tool modu), **K-228** (`en.ts` + `tr.ts` eksiksiz),
   **K-232** (sunucu yanıtı çevrilmez)
3. Önceki fazdan devir: Faz 248'in sayaç tablosu; bu faz ondan yalnız "numara
   yeniden kullanılmaz" kuralını alır
4. Alan hafızası:
   [`hafiza/agent-derleme-ve-onbellek.md`](hafiza/agent-derleme-ve-onbellek.md) (çocuk parmak izi, `:100` sürüm notu) ·
   [`hafiza/workflows.md`](hafiza/workflows.md) (`:76` düğüm sabiti anahtarda, kimlikte değil) ·
   [`hafiza/frontend.md`](hafiza/frontend.md) + [`hafiza/frontend-yerellestirme.md`](hafiza/frontend-yerellestirme.md) (editör) ·
   [`hafiza/nswag-istemci-uretimi.md`](hafiza/nswag-istemci-uretimi.md) (istemci üretimi)
5. Gerektiğinde: reviewer sabiti emsali `AgentDefinitionCompiler.Agents.cs:230-258`
   ve `LoopEvaluatorRegistry.cs:546-552,607`

---

## Amaç

Bir agent başka agent'ları adıyla çağırır (`AgentDefinition.CallableAgentNames`,
`AgentDefinition.cs:105`). Çocuk her zaman güncel sürümünü koşar: derleyici
`ChildAgentInvoker`'ı sabitsiz kurar (`AgentDefinitionCompiler.Agents.cs:197-207`).
Üst agent `v1`'e sabitlense bile (workflow düğümü, onay devamı) çocuk `v3`
koşabilir. Reviewer için sabit zaten vardır (`ReviewerAgentVersion`) ve aynı
çağırıcıyı `pinnedVersion` ile kurar.

- **F-409 (alt agent yarısı)** — tanım her çağrılabilir agent için isteğe bağlı bir
  sürüm taşır: `CallableAgents: [{ name, version? }]`. Sürüm yazılmışsa çocuk o
  sürümü koşar; tool açıklaması ve derleme parmak izi o sürümden gelir. Sürüm
  yoksa bugünkü davranış (güncel sürüm). Üst agent bir sürüme sabitlenince o
  sürümün içeriğindeki çocuk sabitleri de geçerli olur.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinition.cs:105`](../src/Tracon.Abstractions/Agents/AgentDefinition.cs) | `IReadOnlyList<string> CallableAgentNames` — yalnız ad |
| [`AgentDefinitionCompiler.Agents.cs:197-207`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs) | Çocuk çağırıcısı `pinnedVersion` olmadan kurulur |
| [`AgentDefinitionCompiler.Agents.cs:240-252`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs) | Reviewer çağırıcısı aynı tipi sabitle kurar — emsal |
| [`CallableAgentResolver.cs:98-125`](../src/Tracon.Core/Graph/CallableAgentResolver.cs) | Çocuk açıklaması ve descriptor güncel sürümden okunur |
| [`AgentDefinitionCompiler.Dependencies.cs:252-301`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Dependencies.cs) | Parmak izi çocuk açıklamasını içerir (K-811) |
| [`AgentDefinitionPayload.cs:62,159,192`](../src/Tracon.Sql.Shared/Internal/AgentDefinitionPayload.cs) | Saklanan JSON `callableAgentNames` taşır |
| [`AgentContracts.cs:69,126`](../src/Tracon.AspNetCore/Contracts/AgentContracts.cs) | HTTP isteği `CallableAgentNames` taşır |
| `docs-site/src/content/docs/concepts/agents.md:452-460` | "A pin reaches only the step's own agent: the callable agents of that agent still run their current version" |
| Kullanım | `CallableAgentNames`: `src/` içinde 28 kod satırı; `Tracon.UI` 11 dosya; testler 29 dosya; site 15 sayfa (4'ü üretilen şema) |

> Kanıtlar 2026-10-26 tarihinde doğrulandı.

---

## 250.1 — Model

```csharp
// Tracon.Abstractions
public sealed record CallableAgentReference
{
    public required string Name { get; init; }
    /// <summary>The version to run; null runs the agent's current version.</summary>
    public int? Version { get; init; }
}

public sealed record AgentDefinition
{
    // CallableAgentNames silinir
    public IReadOnlyList<CallableAgentReference> CallableAgents { get => field ?? []; init; }
}
```

- `AgentDescriptor.CallableAgentNames` kalır: katalog izdüşümü `CallableAgents`'ın
  adlarıdır (`AgentDescriptorProjection.cs:44`); çağrı grafı ve döngü denetimi
  adla çalışır (`AgentCallGraph.cs:83`).
- Kod agent'ı (`CodeAgentSource.cs:102`) aynı alanı kullanır; kod agent'ı sürüm
  tutmadığı için ona sabit yazılan çocuk kayıtta reddedilir (250.3).

## 250.2 — Kalıcı veri ve HTTP

- `AgentDefinitionPayload` yeni alanı yazar. Okurken yeni alan yoksa ve
  `callableAgentNames` varsa adlar sabitsiz referanslara çevrilir. Migration yok;
  eski satırlar ilk kayıtta yeni biçime geçer. Sözleşme testi eski biçimi okur.
- HTTP isteği (`AgentContracts.cs:69`) `callableAgents` alır. Eski
  `callableAgentNames` alanı `RemovedFieldFilter` (`src/Tracon.AspNetCore/Internal/RemovedFieldFilter.cs`)
  ile `400` ve yeni adı gösteren bir mesaj alır — sessizce yok sayılmaz.
- OpenAPI yenilenir; `Tracon.Client` ve `packages/tracon-client` yeniden üretilir.

## 250.3 — Kayıt doğrulaması

K-989 emsali (`AgentVersionPin.cs:37-80`): sürüm `< 1` → `400`; sürümü olmayan
agent → `400`; sürüm tutmayan kaynak (kod agent'ı) → `400`; aynı ad iki kez →
`400`. Denetim kayıtta ve derlemede yapılır (derleme anında sürüm kaybolduysa
çağrı adıyla düşer — Faz 248 numarayı yeniden kullanmadığı için "kaybolan sürüm"
yalnız silmedir).

## 250.4 — Derleme ve çağrı

- `CreateChildAgents` her çocuğa `pinnedVersion` geçirir (reviewer yolu gibi).
- Çocuğun açıklaması, descriptor'ı ve parmak izi sabit sürümden okunur
  (`CallableAgentResolver.cs:98-125`, `Dependencies.cs:252-301`). Çocuk yeni bir
  sürüm kaydedince sabitli üst agent'ın önbelleği geçersizleşmez; sabitsiz üst
  agent'ınki bugünkü gibi geçersizleşir.
- Üç çağrı modunun üçü de sabiti uygular: varsayılan çağrı, tool modu
  (`SubAgentToolNames.cs:106-145`) ve arka plan (MAF `BackgroundAgentsProvider`, `AgentDefinitionCompiler.Agents.cs:36-38,138-140`) —
  uygulamada her biri ölçülür.
- Replay (`RunReplayService.cs:435`) ve devam (`RunContinuationJobHandler.cs:189,289`)
  yeni alanı temizler/okur.

## 250.5 — Konsol

Agent editörünün çağrılabilir agent bölümü
(`src/Tracon.UI/frontend/src/screens/agent-editor/sections/callable-agents-section.tsx`)
her satırda isteğe bağlı bir sürüm seçimi gösterir. Seçenekler
`GET /tracon/api/agents/{name}/versions`'tan gelir; varsayılan "güncel".
Editör kaydederken sabiti aynen geri yazar (`full-replace-editors.test.tsx`
deseni). Yeni metinler `locales/en.ts` + `tr.ts`. **Bundle:** bugün 225,4 KB gzip
(`index-*.js.br`'den, 2026-10-26) / bütçe 250 KB; payı kapanışta ölçülür ve
buraya yazılır.

## 250.6 — Belge

- `concepts/agents.md:452-460` tersine döner: çocuk güncel sürümü koşar, **tanım
  onu sabitlemediyse**; üst agent sabitlenince o sürümün çocuk sabitleri geçerlidir;
  deney kolu yine yalnız kurulduğu agent'a uygulanır.
- K-895 (c) ve K-989 satırlarına "Faz 250: tanım sabiti" notu düşülür
  (`faz-tamamlama` Adım 8).

---

## Planlanan Public API

```csharp
// Tracon.Abstractions — eklenir
public sealed record CallableAgentReference { string Name; int? Version; }
public IReadOnlyList<CallableAgentReference> AgentDefinition.CallableAgents { get; init; }
// silinir
// Tracon.AgentDefinition.CallableAgentNames.get / .init
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` / `PUT` | `/api/agents` · `/api/agents/{name}` | Operator | Gövde `callableAgents: [{name, version?}]`; eski `callableAgentNames` → `400` |
| `GET` | `/api/agents/{name}` · `/api/agents/{name}/versions` | Reader | Tanım `callableAgents` döner |

### Arayüz payı

Ölçülmeli (250.5); bugünkü kullanım 225,4 KB / 250 KB gzip.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Agents/{AgentDefinition,CallableAgentReference}.cs
src/Tracon.Abstractions/PublicAPI.Unshipped.txt
src/Tracon.Core/Compilation/{AgentDefinitionCompiler.Agents,AgentDefinitionCompiler.Dependencies,AgentDefinitionValidator}.cs
src/Tracon.Core/Graph/{CallableAgentResolver,SubAgentToolNames,AgentCallGraph}.cs
src/Tracon.Core/Catalog/{AgentDescriptorProjection,CodeAgentSource,CompositeAgentCatalog}.cs
src/Tracon.Core/Replay/RunReplayService.cs · Scheduling/RunContinuationJobHandler.cs
src/Tracon.Sql.Shared/Internal/AgentDefinitionPayload.cs
src/Tracon.AspNetCore/Contracts/AgentContracts.cs · Endpoints/AgentEndpoints.cs · Internal/RemovedFieldFilter.cs
src/Tracon.UI/frontend/src/screens/agent-editor/{model.ts,use-agent-editor.ts,sections/callable-agents-section.tsx}
src/Tracon.UI/frontend/src/locales/{en,tr}.ts
src/Tracon.Client (üretilir) · packages/tracon-client (üretilir)
src/Tracon.Testing.Contracts.Xunit/Contracts/AgentDefinitionStoreContract.cs
samples/Tracon.Samples.CustomAgentSource · samples/Tracon.Api (kullanım varsa)
docs-site/src/content/docs/concepts/{agents,workflows}.md · ui.md
CHANGELOG.md
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Sabitli çocuk güncel sürümü koşar (**önce kırmızı**: çocuk `v2` kaydedildikten sonra `v1` sabiti) | Fonksiyonel | `CallableAgentVersionPinTests` (yeni; çocuk run satırı `agentVersion = 1`) |
| Üst agent `v1`'e sabitlenince çocuk sabitleri uygulanmaz | Fonksiyonel | `WorkflowAgentVersionPinTests` yeni case |
| Modele giden tool açıklaması güncel çocuktan gelir | Fonksiyonel | `CallableAgentVersionPinTests` (model isteğindeki açıklama `v1`'inki) |
| Tool modu ya da arka plan modu sabiti atlar | Fonksiyonel | aynı sınıf, mod başına case |
| Olmayan sürüm, `< 1`, kod agent'ına sabit, tekrar eden ad kaydedilir | Fonksiyonel (HTTP) | `AgentCrudTests` / doğrulayıcı testleri |
| Eski `callableAgentNames` HTTP alanı sessizce yok sayılır | Fonksiyonel (HTTP) | `RemovedFieldFilter` testi |
| Eski saklanan JSON okunamaz | Sözleşme + entegrasyon | `AgentDefinitionStoreContract` (eski biçim) + `AgentDefinitionPayloadRoundTripTests` (Sqlite) |
| Sabitli üst agent'ın önbelleği çocuk kaydında gereksiz geçersizleşir ya da sabitsiz olanınki geçersizleşmez | Birim | parmak izi testleri (`AgentDefinitionCompiler.Dependencies`) |
| Replay ya da devam yeni alanı kaçırır | Birim | `RunReplayService` / `RunContinuationJobHandler` testleri |
| Çağrı grafı döngüsü sabitle kaçar | Birim | `AgentCallGraph` testleri (adla) |
| Editör kaydederken sabiti siler | Vitest | `full-replace-editors.test.tsx`, `agent-editor.test.tsx` |
| Editörde sürüm seçimi | E2E | `Tracon.Ui.E2ETests` agent editörü sınıfı |
| `en.ts`/`tr.ts` eksik anahtar | Derleme (K-228) | frontend build |

Beş soru: **iptal** — değişmez · **eşzamanlılık** — çocuk sürümü çağrı anında
sabitten çözülür, paylaşılan durum yok · **boş/aşırı girdi** — boş liste =
çocuk yok; sürüm `null` = güncel · **başka kiracı** — çocuk kiracının kataloğundan
çözülür (bugünkü yol); başka kiracının sürümü görünmez · **alt sistem hatası** —
sabit sürüm silindiyse çağrı adıyla düşer.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 🤖 | `samples/Tracon.Api`, iki DB agent'ı (`writer` → `researcher`) | `researcher`'ı iki kez kaydet; `writer`'ı `callableAgents: [{name:"researcher", version:1}]` ile kaydet; `writer`'ı koştur | Çocuk run satırı `agentVersion: 1` |
| 2 🤖 | Aynı kurulum, konsol | Editörde `researcher` satırında sürüm `1` seç, kaydet, yeniden aç | Seçim korunur; `GET /tracon/api/agents/writer` `version: 1` döner |

Case'ler `docs/manuel-test/29-AGENT-DESTEGI.md` ve `10-ARAYUZ-AGENT-PLAYGROUND.md`'ye eklenir.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sabit kayıtta mı yalnız derlemede mi denetlenir | A: ikisinde (K-989 gibi) · B: yalnız derlemede | A |
| 2 | `AgentDescriptor` sabiti de gösterir mi (katalog listesi) | A: yalnız adlar kalır · B: `CallableAgents` izdüşümü eklenir | A — katalog sözleşmesi büyümez; sabit tanımda okunur |
| 3 | `GET …/versions` sürüm seçimine yetmezse (sayfalama) | A: son N sürüm + elle sayı girişi · B: tam liste | A; ölçülmeli |

---

## Kapsam Dışı

- Skill sürüm sabiti — K-082 (kullanıcı kararı) yeniden açılmadan yapılamaz; F-698.
- Deney kolunun çocuklara yayılması — bilinçli olarak yok (site `agents.md:452-460`).
- Saklanan workflow düğümünün alt agent sabiti — düğüm zaten üst agent'ı sabitler; çocuklar o sürümün tanımından gelir.

---

## Bitiş Ölçütleri (DoD)

- [ ] `callableAgents: [{name, version: 1}]` ile çocuk `v1` koşar; açıklama ve parmak izi `v1`'den gelir; üç çağrı modunda da
- [ ] Sabitsiz çocuk güncel sürümü koşar (değişmez davranış)
- [ ] Eski JSON okunur; eski HTTP alanı `400` ve yeni adla cevaplanır
- [ ] Konsol editörü sabiti gösterir, düzenletir ve korur; `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı
- [ ] İki istemci yeniden üretildi; `PublicAPI.Unshipped.txt` güncel
- [ ] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`)
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `🤖` case'ler ilgili `docs/manuel-test/` dosyalarında ve koşuldu; gerisi otomatik testte
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (250.6); `npm run build` + `check-links.mjs` temiz; `CHANGELOG.md` *Changed* + *Added* yazıldı
- [ ] Karar `docs/KARARLAR.md`'ye yazıldı (`public-api` + `kalıcı-veri`; reddedilen alternatifler: yan sözlük `CallableAgentVersions`, kayıtta kilit dosyası, zamana göre gizli sabit)

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*CallableAgentVersionPinTests*"
curl -s "http://localhost:5081/tracon/api/runs?parentRunId=<runId>" | jq '.. | .agentVersion? // empty'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Eski istemci `callableAgentNames` gönderir ve çocuklar sessizce düşer | `RemovedFieldFilter` `400` döner; `CHANGELOG` *Changed* |
| Saklanan eski JSON yanlış okunur, çocuklar kaybolur | Okuma çevirisi + sözleşme testi eski biçimle |
| Editör sabiti siler | Vitest tam yerine koyma testi |
| Bundle bütçesi (24,6 KB boşluk) | Sürüm seçimi mevcut bileşenlerle yazılır; pay ölçülür |
| Parmak izi değişikliği önbelleği gereksiz boşaltır | Birim testi iki yönü ölçer |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.
