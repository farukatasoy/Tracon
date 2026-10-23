# Plana Dönüşen Adayların Gövdeleri

> **Kapanmış kayıt.** Baştan sona okunmaz — yalnız `grep`'lenir.
>
> Bir aday kalem faza dönüştüğünde [`../ADAYLAR.md`](../ADAYLAR.md)'den
> silinir; kalemin **tam gövdesi** buraya taşınır. 2026-09-24'ten itibaren
> aday dosyasında yönlendirici satır da kalmaz — iz faz dokümanının
> `> **Kaynak:** … F-NN` satırıdır.
> Gerekçe: aday dosyası 80 KB bütçesindedir ve her oturumda okunur; plana
> dönüşmüş bir kalemin gerekçesi artık **fazın kendi dokümanındadır**.
>
> **Bir kalemin bugünkü doğrusu burada değildir.** Gövdeler taşındıkları
> tarihte doğruydu; `dosya:satır` kanıtları o günün deposuna aittir. Güncel
> plan için fazın dokümanına bak.

| Nerede ne yaşar | |
|---|---|
| Kalemin uygulanabilir planı | `docs/NN-*.md` |
| Kalemin izi | Faz dokümanının `> **Kaynak:**` satırı — `grep -rn "F-NNN" docs/` |
| Kalemin aday gövdesi (bu dosya) | taşındığı tarihle |
| Turun tam kaydı | [`../kesif/`](../kesif/) |

---

## 2026-10-22 — F-617 → Faz 235, F-616 → Faz 236

Taşındıkları gün `docs/ADAYLAR.md`'den alınan gövdeler. Güncel plan: Faz 235, [`../236-AGUI-UCU.md`](../236-AGUI-UCU.md).

### F-617 · MCP elicitation: MRTR ile askıya alınan tool çağrısı

**Kanal:** faz planlama · 🟢

**Sorun:** Tracon MCP istemcisini `clientOptions: null` ile kurar (`McpConnection.cs:174`, `McpShortLivedConnection.cs:63`, `McpOAuthAuthorizationCoordinator.cs:236`); elicitation yeteneği bildirilmez, kullanıcıdan bilgi isteyen sunucunun tool'u düşer.

**Kapsam:** MCP 2026-07-28 MRTR yolu: sunucu `InputRequiredResult` döner; Tracon run'ı onay gibi askıya alır (bekleyen istek + run olayı); cevap sonraki turun girdisidir; tool çağrısı `RequestParams.InputResponses` ve `RequestState` ile yeniden gönderilir. Eski (doğrudan istek) sunucu: bilerek reddet ya da yalnız canlı yüzeyde zaman aşımlı bekle (karar). Alt agent K-103 ile reddeder.

**Değer:** Kullanıcıdan onay veya bilgi isteyen MCP sunucuları Tracon altında çalışır.

**Genellik:** MCP bir ekosistem standardıdır; elicitation spec'in çekirdek istemci yeteneğidir. Tüketici kodunda kalamaz: bağlantı Tracon'un.

**Mercek:** 3, 6.

**Hazırlık:** SDK 2.2 MRTR'yi taşır; `InputResponses`/`RequestState` public (decompile, 2026-10-21). Tracon'un onay askısı hazır.

**Maliyet:** Bekleyen istek tipi, run olayı, HTTP cevap yolu, konsol formu, TS istemcisi.

**Risk:** Bağlantı (kiracı, sunucu) başına paylaşılır; eski sunucunun doğrudan isteği çağıran run'ı bilmez (okuma). URL modu elicitation dış tarayıcı açar.

**Bağımlılık:** —

**Ekosistem (2026-10-21):** MCP spec 2026-07-28 changelog: MRTR sunucudan istemciye isteğin yerini alır; tasks uzantıya taşındı.

**Karşı görüş:** Elicitation kullanan sunucu sayısı ölçülmedi; çoğu sunucu tool parametresiyle yetinir.


### F-616 · Tracon'un AG-UI ucu

**Kanal:** faz planlama · 🟢

**Sorun:** AG-UI ön yüzü (CopilotKit gibi) Tracon agent'ına bağlanamaz. MAF'ın `MapAGUIServer`'ı Tracon altında çalışmaz: istemci tool'unun devam turunu çalışan vekil tool ve SDK'nın ürettiği onay çiftiyle kurar; K-1039 kuralı vekili reddeder (`RunChatOptionsRule.cs:66`), girdide gelen onay çifti tool'u koşturmaz (probe, 2026-10-21; [`MAF-KAPSAMA.md`](../MAF-KAPSAMA.md) §0 #37).

**Kapsam:** `AGUI.Server`'ın olay dönüştürücüsü (`AsAGUIEventStreamAsync`) üzerinde Tracon ucu; girdi eşlemesi Tracon'un kendi mekanizmalarına: istemci tool sonucu → `toolResults` (kodda kayıtlı istemci tool'u, `AddClientTool`), onay resume → `ToolApprovalResolver` (sunucudaki bekleyen istekle eşleme, denetim satırı); oturum F-614'ün izolasyonlu deposundan.

**Değer:** AG-UI ön yüzleri Tracon'un run kaydı, kiracı, onay ve maliyet sınırıyla çalışır.

**Genellik:** AG-UI bir ekosistem standardıdır; MAF onu birinci sınıf entegrasyon olarak sunar. Tüketici kodunda kalamaz: ucu yazan Tracon'un onay ve istemci tool sözleşmesini yeniden kurmak zorunda kalır.

**Mercek:** 1, 6.

**Hazırlık:** `AGUI.Server` 1.0.1 kararlı (bağımlılık yalnız MEAI.Abstractions); Tracon'un istemci tool'u ve onay çözücüsü hazır.

**Maliyet:** Yeni uç ve kayıt uzantısı; +2 paket; konsol yok.

**Risk:** AG-UI istemcisi tool'u istek başına beyan eder, Tracon kodda beyan eder (K2 sınırının genişlemesi); karar: yalnız kodda kayıtlı istemci tool'uyla kesişim mi. `RunAgentInput.State` eşlemesi açık.

**Bağımlılık:** F-614 (izolasyon sağlayıcısı ve anahtarlı oturum deposu).

**Ekosistem (2026-10-21):** MS Learn AG-UI entegrasyonu yedi özelliği (onay, ön yüz tool'u, durum) kapsar; `AGUI.Server` yayıncısı "AG-UI Protocol"dür → `nuget-danismani` tedarik zinciri onayı.

**Karşı görüş:** Tracon'un kendi gömülebilir sohbet aracı ve TS istemcisi aynı işi görür; AG-UI talebi ölçülmedi. Değer, ekosistemin AG-UI'yi standart kabul etmesine bağlıdır.
