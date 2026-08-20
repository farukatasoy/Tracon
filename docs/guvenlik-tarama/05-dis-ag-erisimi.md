# Konu 5 — Dış Ağ Erişimi: SSRF, Webhook, MCP/A2A

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`SocketsHttpHandler.ConnectCallback` kullanılan dosyalar, webhook imza
doğrulama kodu, `src/AgentPrism.Mcp/`, OAuth token saklama kodu.

## Bilinen tasarım

Adres denetimi `ConnectCallback`'e gömülü (DNS çözümü ile bağlantı arasına
girmez, TOCTOU'yu önler); varsayılan `AllowPrivateNetworkTargets=false`,
yalnız `https`, redirect kapalı (K-164/163/165). Webhook imzası
`HMAC-SHA256(timestamp+"."+body)` (K-158/159). MCP yalnız HTTP (stdio yok),
kimlik değeri değil yapılandırma anahtarının ADI saklanır, OAuth token
bellek-içi (DB'ye yazılmaz) (K-058/059/060, K-168-171).

## Ara

- "OAuth token DB'ye yazılmaz" iddiasını KODDA doğrula — `grep` ile token'ın
  geçtiği her `INSERT`/`UPDATE`/store çağrısını bul, hiçbirinin token alanını
  kalıcı depoya yazmadığını kanıtla.
- DNS rebinding senaryosunu: `ConnectCallback` içindeki adres kontrolü ile
  gerçek `Connect` çağrısı arasında DNS yeniden çözümü olabilir mi (TOCTOU
  penceresi teoride kapalı olmalı, kodda gerçekten öyle mi).
- HMAC imza doğrulamasının sabit-zamanlı karşılaştırma kullandığını (timing
  attack riski).
- F-91 (ADAYLAR.md, açık): "MCP OAuth token'ının örnekler arasında
  paylaşılması" — bu hâlâ açık bir tasarım sorusu, yeniden çözme, yalnız
  mevcut kodun bunu nasıl ele aldığını gözlemle ve bildir.
