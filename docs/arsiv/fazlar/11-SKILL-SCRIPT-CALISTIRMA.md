# Faz 11 — Skill Script Çalıştırma

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-09** (2/2)
> **Önkoşul:** [Faz 9](09-YONETISIM-VE-DENETIM-IZI.md) **ve** [Faz 10](10-AGENT-SKILLERI.md) — ikisi de zorunlu
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0004 (`0004_skill_scripts.sql`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/11-SKILL-SCRIPT-CALISTIRMA.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## ⚠️ Bu Faz Bir Güvenlik Sınırını Değiştirir

Tasarım kuralı **K2** şunu der: *"Tool'lar yalnız kodda tanımlanır. Arayüze
erişen herkes sunucuda kod çalıştırabilseydi bu bir güvenlik açığı olurdu."*

Bu faz, K2'nin **ikinci bilinçli istisnasıdır**. Birincisi MCP'ydi (K-058) ve
orada süreç **uzakta** çalışıyordu. Burada süreç **AgentPrism'in makinesinde**
çalışır. Fark budur ve bu fazın tüm tasarımı bu farkı yönetmek üzerinedir.

**Kullanıcı kararı (2026-08-02):** script çalıştırma kabul edilebilir (K-066).
Karar, kontrolsüz çalıştırma anlamına gelmez.

---

## Faz 10'dan Devralınan Sözleşmeler

Faz 10 tamamlandı. Önce bu yüzeyleri oku; script desteği bunları genişletecek,
yerine paralel bir skill zinciri kurmayacaktır.

| Sözleşme | Mevcut davranış |
|----------|-----------------|
| `AgentSkillDefinition` | `Instructions`, frontmatter, `Resources`, `Enabled`, `Version`, UTC zamanları taşır. Script alanı yoktur. |
| `IAgentSkillStore` | `ListAsync(tenantId)`, `GetAsync(tenantId, name)`, `SaveAsync(skill)`, `DeleteAsync(tenantId, name)`; PostgreSQL kaynakları `agent_skill_resources` tablosunda cascade bağlıdır. |
| `AgentSkillCatalog` | Kod kaydı store kaydını aynı adda geçersiz kılar. Bilinmeyen skill derleme hatasıdır; `Enabled = false` MAF'a girmez. |
| `AgentPrismSkillsSource` | `AgentSkillDefinition` değerini `AgentInlineSkill`e çevirir. Kaynak zinciri `Aggregating` → `Filtering` → tenant anahtarlı `Caching` → `Deduplicating` biçimindedir. |
| `AgentDefinition.SkillNames` | Agent tanımının sürümlü JSON yükündedir. `CompiledAgentCache` anahtarı skill parmak izini içerir; script ekleme bu geçersiz kılma davranışını korumalıdır. |
| Onay | `AgentSkillsProviderOptions.Disable*Approval` değerleri ayarlanmaz. Gerçek OpenRouter denemesinde `load_skill` onayı Playground'da göründü ve onaylanınca skill talimatı yüklendi. |

🚨 `AgentInlineSkill.AddScript` Faz 10'da bilerek çağrılmadı. Faz 11 ekleme
yaparsa `AgentSkillCatalog` ve `AgentPrismSkillsSource` üzerinden gitmeli;
MAF'ın dosya tabanlı kaynaklarını doğrudan veritabanı verisi için kullanmak
tenant yalıtımını ve cache parmak izini atlar.

---

## Bu Fazda Verilen Kararlar

| Karar | Konu |
|-------|------|
| **K-086** | K2'nin ikinci istisnası; `PlatformIsolationAcknowledged` şartı; sağlanan ve **sağlanmayan** korumaların listesi |
| **K-087** | Hem dosya tabanlı hem saklanan script'ler; script kökü koddan gelir |
| **K-088** | Yorumlayıcı beyaz listesi boş varsayılan |
| **K-089** | Denetim izi yazılamazsa çalıştırma reddedilir |
| **K-090** | Sığ argüman doğrulaması; yeni bağımlılık yok |
| **K-091** | Argümanlar stdin ile geçirilir; ortam sıfırlanır |
| **K-092** | İzin kaydı silinmez, iptal edilir; `COALESCE` benzersizlik indeksi |

---

## Açık Soruların Cevapları (kullanıcı kararı, 2026-08-02)

1. **Seçenek B uygulanacak mı?** → **Evet, A + B birlikte.** Dosya tabanlı
   kaynak (`AgentFileSkillsSource`) kökleri **yalnız kodda** verilir; saklanan
   script'ler ayrıca `AllowStoredScripts` bayrağıyla kapılıdır (K-087).
2. **Hangi yorumlayıcılar?** → **`python3` + `node` + `bash`.** Üçü de
   desteklenir ancak hiçbiri kendiliğinden kayıtlı değildir (K-088).
3. **Eşzamanlılık?** → Kiracı başına **2**, toplam **8**;
   `SkillScriptConcurrencyLimiter` iki katmanlı `SemaphoreSlim` kullanır.
4. **`AllowedTools` zorlansın mı?** → **Evet.**

---

## Plandan Sapmalar

| Sapma | Gerekçe |
|-------|---------|
| Seçenek B (arayüzde script yazma) da uygulandı | Kullanıcı kararı. Arayüz script **içeriği** yazabilir ama script **kökü** ekleyemez; kök keyfî dosya sistemi okuması demek olurdu (K-087). |
| Üç yorumlayıcı desteklendi, yalnız `python3` değil | Kullanıcı kararı. Beyaz liste boş varsayıldığı için ek risk kurulum anında bilinçli olarak alınır (K-088). |
| JSON Schema doğrulaması sığ yapıldı | Tam doğrulayıcı yeni bir NuGet bağımlılığı gerektirirdi; kütüphane tüketicinin bağımlılık grafiğini kirletmez (K-090). |
| İzin uçlarında `TimeProvider` yerine `DateTimeOffset.UtcNow` | `TimeProvider` DI'da kayıtlı olmadığı için minimal API metadata çıkarımı tüm uçları kırıyordu. |

---

## Gerçek Çalıştırma Kanıtı

Test: `SandboxedSkillScriptRunnerTests.Izinli_script_gercekten_calisir_ve_denetim_izine_yazilir`

Kurulum: `Enabled = true`, `PlatformIsolationAcknowledged = true`,
`AllowStoredScripts = true`, `Interpreters["sh"] = "/bin/bash"`, kiracı
`default` için skill geneli izin.

Script içeriği:

```sh
echo merhaba-agentprism
```

Modele dönen çıktı:

```text
merhaba-agentprism
```

Denetim izi satırı: `action = "script.run"`, `tenantId = "default"`.
İzin kaldırıldığında aynı çağrı `AgentPrismException` ile reddedilir ve
`action = "script.denied"` yazılır.

Koşum sonucu (macOS arm64, .NET 10):

```text
AgentPrism.Core.UnitTests            149 passed, 0 failed
AgentPrism.PostgreSql.IntegrationTests 156 passed, 0 failed
AgentPrism.AspNetCore.FunctionalTests  136 passed, 0 failed
```

---

## Bitiş Ölçütleri (DoD)

- [x] Yapılandırma yapılmamış bir kurulumda script çalıştırma **kapalı** ve
      denendiğinde anlaşılır bir hata veriyor
- [x] Beyaz listedeki bir yorumlayıcı ile gerçek bir script onaydan geçip
      çalışıyor; çıktısı modele dönüyor (gerçek çıktı yukarıda)
- [x] Zaman aşımı, çıktı sınırı ve ortam temizliği testlerle kanıtlı
- [x] İzinsiz script çalışmıyor; reddedilme denetim izinde görünüyor
- [x] `tool_invocations`, span ve metrik dolduruluyor
- [x] README ve `MIMARI.md` sağlanamayan izolasyon sınırlarını **açıkça** yazıyor
- [x] Dört doğrulama kapısı sıfır uyarı; sır taraması boş

---

## Sonraki Faza Devir Notu

- Faz 12 (agent'ın agent'ı çağırması) benzer bir "kaynak sınırı" sorusuyla
  gelir. Buradaki eşzamanlılık sınırı deseni oraya taşınabilir.
- Faz 25 (saklama) süresi dolmuş `skill_script_grants` kayıtlarını temizlemekle
  yükümlüdür.
- Faz 7 (yayın) yapılırken bu fazın public API'si **güvenlik yüzeyi** olarak
  ayrıca gözden geçirilmelidir.
