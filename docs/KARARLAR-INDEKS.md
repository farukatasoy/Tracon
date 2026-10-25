# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul ve oku (karar tek satırdır): `grep -n 'K-059 —' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 1027 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (52 / 1079 kalem)

| K | Karar |
|---|---|
| K-1038 | `EnableMessageInjection`, `RequirePerServiceCallChatHistoryPersistence`, `RawRepresentationFactory` korunur: Tracon'da işlemez ya da tanımı ezer 👤 |
| K-1039 | Run başına `ChatOptions` HTTP'de yoktur; kod ve MAF yolu tek kuraldan geçer: tanımın üyesi reddedilir, ek talimat ve tool beyanı geçer 👤🔁 |
| K-1040 | Model istek alanları tanımdadır; sağlayıcı yüzeyi gönderdiğini `ISamplingSupport` ile bildirir, bildirmeyen alan derleme hatasıdır (F-598) |
| K-1041 | `AllowConcurrentToolCalls` `ModelBinding.ToolLoop` altına taşındı; eski yük okumada çevrilir, eski HTTP anahtarı `400` alır (F-598) 👤 |
| K-1042 | Run seçeneği `AllowMultipleToolCalls = false` geçer; `true` ve öteki korunan üyeler reddedilir (K-1039 daraltması) |
| K-1043 | Alt agent çağrı modu tanımdadır: `SubAgentSettings.CallMode` (`Background`, `Tool`); `Tool`'da ad çakışması reddedilir (F-599) |
| K-1044 | Hosted tool kodda kaydedilir, tanım adla seçer: `AddHostedTool`; tool zinciri yoktur, tür başına en çok bir tane (F-603) |
| K-1045 | Hosted tool desteğini sağlayıcı `IHostedToolSupport` ile bildirir; bildirmeyen tür derleme hatasıdır (F-603) |
| K-1046 | Kayıtlı hosted çağrı `tracon.content` değil `ToolInvoking`/`ToolInvoked` olur; kayıtsız olan K-1034'te kalır (F-603) |
| K-1047 | Hosted çağrı ücreti iç döngü halkasında, yanıt önbelleğinin içinde raporlanır; fiyatsız tür `null` kalır, `0` olmaz (F-603) |
| K-1048 | `EvalCase.ExpectedTools` yerine `ExpectedToolCalls` (ad + isteğe bağlı argüman); eski HTTP anahtarı `400` alır (F-605) 👤 |
| K-1049 | `toolCallArgsMatch` Tracon'un kendi kontrolüdür: değeri JSON anlamıyla karşılaştırır; beklenti `EvalItem`'a iki biçimde verilir (F-605) |
| K-1050 | `eval_cases.expected_tool_calls` yeni sütun; eski `expected_tools` yazılmaz, okunur, düşürülmez; okunamayan metin vakayı düşürür (F-605) |
| K-1051 | Workflow adımında tool onayı reddedilmez; MAF'ın dış istek portuyla workflow isteğine döner (K-103'ün workflow kapsamı) 👤 |
| K-1052 | A2A oturum kimliği `a2a:` + SHA-256 özeti: kiracı, agent, çağıran, `contextId`; her parça uzunluk önekli (F-614, Faz 229) |
| K-1053 | A2A çağıranı tür önekli kimlik: `user:` · `apikey:` · `static-token` · `anonymous`; okunamayan kullanıcı reddedilir (F-614, Faz 229) |
| K-1054 | Sahiplik açıkken A2A mesajı, atıf kimliği yoksa ya da konuşma başka kullanıcınınsa model çağrılmadan reddedilir (F-614, Faz 229) |
| K-1055 | Workflow adımı tool onayını yalnız MAF'ın host ettiği ve ölçülen düğümde bekler; fonksiyon adımı ve Magentic reddi sürdürür 🔁 |
| K-1056 | `ToolApproval` cevabı yalnız karar taşır; araç çağrısı kayıtlı istekten gelir, `data` reddedilir |
| K-1057 | Onay kuralı ve denetim satırı tek `ToolApprovalDecisionRecorder`'dan; ajan adı executor kimliğinden çözülür |
| K-1058 | Cevaplanmayan istek süresi dolunca bekleyen adım run'ı workflow'la `Failed` kapanır; cevaplanan `AwaitingApproval` kalır |
| K-1059 | `ToolApproval` isteği workflow hangi cevap kipinde olursa olsun tek cevap alır; ikinci aynı karar ilkini izler, farklısı `409` (F-615, Faz 230) |
| K-1060 | Magentic katılımcısı tool onayını workflow isteği olarak bekler; yöneticinin onayı reddi sürdürür (K-1055 daraltması) |
| K-1061 | Skill okuma onayını tanım kapatır, script onayını tanımdaki hiçbir ayar kapatmaz; dış yüzey kapısı o ayarı okur (F-626) 👤 |
| K-1062 | Uzantı model zincirine halkayı `UseChatClient(ChatClientSlot, Func<IChatClient, IChatClient>)` ile koyar; seçim `ExtensionNames` ile (F-594) |
| K-1063 | Halka iki sabit yuvaya girer, içerik korumasının altına inemez; yerleştiremeyen registry derlemeyi durdurur (F-594) |
| K-1064 | Tanımın koleksiyon üyeleri tipin `get`'inde `?? []` ile boş döner; okuyucuya onarım listesi yazdırılmaz |
| K-1065 | Konuşma hafızası Core'dadır: kapsam kiracı + agent + kullanıcı; kullanıcısız ya da oturumsuz run hafıza kullanmaz (F-627) |
| K-1066 | Hafıza kapsamı her çağrıda run'dan okunur; MAF sağlayıcısına oturum verilmez (F-627) |
| K-1067 | Hafıza kaydı oturumun türetilmiş kopyasıdır: oturumla birlikte silinir, önce kopya sonra satır (F-627) |
| K-1068 | Oturum alanının yol biçimi kalıcıdır: `{kiracı}/~workspace/{anahtar}/{agent}/…`, anahtar SHA-256(kiracı, oturum) ilk 32 hex (F-628) |
| K-1069 | Agent oturum başına dosya alanı alır: tool'lar Tracon zincirinden geçer, yazma ve silme hep onay ister, kota var; K-062'nin yerini alır (F-628) |
| K-1070 | Veri sahibi silmede oturum kopyaları SQL işlemi açılmadan ÖNCE silinir; K-1067'nin sırasını değiştirir (F-628) |
| K-1071 | MCP elicitation: araç çağrısı askıya alınır ve cevapla BAŞTAN yeniden çağrılır; çağrı açık bekletilmez 👤 |
| K-1072 | Run bağlamı olmayan elicitation isteği (oturumsuz, alt agent, kanalsız yüzey, eski protokol, url modu) decline alır; sessiz düşme yok |
| K-1073 | Elicitation cevabı sorulan soruya bağlıdır (message+schema parmak izi); başka soru sorulursa decline, çağrı en çok bir kez askıya alınır |
| K-1074 | tool_input.answer fail-closed denetim kümesinin sekizinci işlemidir (7 → 8); satır alan adlarını taşır, değeri taşımaz |
| K-1075 | Yeniden çağrı MAF'ın bekleyen-istek kaydına koşu KAPANIRKEN yazılır (_pendingApprovalRequests); anahtar MAF'ın public yüzeyi değildir |
| K-1076 | AG-UI ucu ayrı `Tracon.AGUI` paketidir; AG-UI SDK'sı yalnız orada, meta pakete girmez 👤 |
| K-1077 | AG-UI isteğindeki tool yalnız kodda kayıtlı istemci tool'uyla kesişirse kullanılır; şema koddan gelir 👤 |
| K-1078 | `rc.1` sonrası aynı major'da kırılma notla kurtarılmaz; yama (aynı major.minor, rc→GA dahil) yüzeyi büyütmez (F-559, Faz 237) 👤 |
| K-1079 | Devam run'ı bütçeyi zincirden devralır: cevap, resume ve kurtarma aynı kuralı alır; süre yalnız aktif yürütmedir (F-531) 👤 |
| K-1080 | Kiracı kotası çalışan ağaçta düğüm ve agent adımı başlamadan okunur: sayaç + ağacın yazılmamış payı (F-529) 👤 |
| K-1081 | HTTP, SSE, telemetri, yapılandırma, hata kodu ve CLI sürümden sürüme kapıyla korunur: kendi betiğimiz, `oasdiff` değil (F-559, Faz 238) 👤 |
| K-1082 | `IncludeTenantTag` varsayılan kapalı: `tracon.tenant.id` metrikte isteğe bağlı; span ve kota göstergesi her zaman taşır (F-559, A-68) 👤 |
| K-1083 | Hata kodları iki katalogdadır: `TraconErrorTypes` (public), `ValidationCodes` (internal); değerler değişmez (F-559, Faz 238) 👤 |
| K-1084 | Kiracı başına workflow sınırı süreçte tam, kuyrukta yumuşaktır; tek ayar iki katmanı besler, kiracı slotu ortaktan önce alınır (F-322) 👤 |
| K-1085 | Slot bekleyen workflow run'ı `Queued` yazılır, heartbeat taşır ve uzlaştırmada kapanır; heartbeat'siz `Queued` kuyruğundur (F-322) 👤 |
| K-1086 | Native agent akışı Loop iterasyon sınırını `loop.iteration-completed` çerçevesiyle verir; veri `/events` ile bayt bayt aynıdır (F-544) |
| K-1087 | Yerleşik `agentReview` ölçütü katalog agent'ını ad (+sürüm) ile anar, çocuk run koşar; karar şekli reviewer'ın tanımındadır (F-411) |
| K-1088 | Loop iterasyon olayı `criteria[]` ile ölçüt kararını ve onaylayanın kimliğini yazar (yargıç, reviewer, çocuk run); metin yazmaz (F-411) |
| K-1089 | Tükenmiş ağaç bütçesinde tool çağrısı başlamaz: token, maliyet ve süre; ret `run_budget_exceeded` kimliğini korur (F-530) |
