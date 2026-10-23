# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul ve oku (karar tek satırdır): `grep -n 'K-059 —' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 1013 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (52 / 1065 kalem)

| K | Karar |
|---|---|
| K-1024 | `WorkflowRunRequest.DefinitionVersion` public; checkpoint'siz kurtarma yeniden başlatması zincirin ilk run'ının sürümünü sabitler (F-492) |
| K-1025 | `/v1/chat/completions` görsel, ses ve dosyayı modele verir; tool geçmişi ve bilinmeyen rol `400` (F-580) (kullanıcı kararı) 👤 |
| K-1026 | OpenAI ses istemcisi `OpenAIClient.Pipeline` üstündedir, `AudioClient`/MEAI adaptörü üstünde değil (F-579) |
| K-1027 | `/v1/chat/completions` agent'ın istek ayarını `400` ile reddeder; `/v1/conversations` ilk `items`'ı reddeder (F-581) |
| K-1028 | `RunRecordingAgent` iç agent'a düz `AgentRunOptions` verir, `TraconRunOptions` değil (G-07) |
| K-1029 | Agent uzantısı MAF seçenek nesnesinin kendisini alır; korunan üye tablosu ve ratchet testi sınırı zorlar (F-588) |
| K-1030 | Uzantıyı kod kaydeder, tanım yalnız adla seçer: `AddAgentExtension`, `ExtensionNames` (F-588) |
| K-1031 | SQL payload'ında sonradan gelen liste alanı null okunabilir; boş liste yazılmaz (F-588) |
| K-1032 | Sohbet agent'ında `RequirePerServiceCallChatHistoryPersistence` serbesttir; açıksa model istemcisi sınır seam'inden geçer (F-588) 🔁 |
| K-1033 | Tool oynatan yollar uzantıyı çalıştırmaz: replay `ExtensionNames`'i boşaltır, continuation reddeder (F-588) |
| K-1034 | Tanınmayan içerik `tracon.content`, workflow olayı `tracon.workflow.event` olarak kaydedilir; yük gövdesizdir (F-589) |
| K-1035 | `UseProvidedChatClientAsIs` ve `AllowConcurrentInvocation` korunur: ikisi korunan bir ayarı dolaylı değiştirir |
| K-1036 | Deneysel upstream tip yalnız ön sürüm hattındaki uydu paketin public imzasına girer; çekirdek aile kararlı kalır (D-01) |
| K-1037 | Ön sürüm upstream'e bağlı yeni uydu paket ayrı ön sürüm hattında çıkar; mevcut aile tek hatta kalır (D-19) |
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
