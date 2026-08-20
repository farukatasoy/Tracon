# Konu 10 — Loglama/Trace ve Gizlilik Sızıntısı

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`RunTraceCollector`, span/trace örnekleme kodu, genel `ILogger` kullanım
noktaları (özellikle `catch` blokları).

## Bilinen tasarım

Varsayılan span örnekleme oranı 0,1; `MaxSpansPerRun=200` (K-056).

## Ara

- Prompt/tool-argüman içeriğinin `ILogger` çağrılarına (özellikle hata
  loglarında, exception mesajlarında, stack trace'lerde) maskesiz
  sızıp sızmadığını.
- `SuccessSampleRatio=1` ayarıyla davranışın (tüm span'lerin loglanması)
  hassas veri tutma süresini nasıl etkilediğini — bu bir dokümantasyon
  uyarısı gerektiriyor mu.
