# Konu 4 — Script/Tool Çalıştırma Sandbox'ı

Ortak çerçeve: [`00-INDEKS.md`](00-INDEKS.md). Bu oturum onu uygular.

## Kapsam

`src/AgentPrism.Core/Skills/Scripts/SandboxedSkillScriptRunner.cs` ve
çağıranları, feature flag ve kota kodu.

## Bilinen tasarım

6 kapılı zincir: Enabled → kiracı izni → yorumlayıcı allow-list (varsayılan
BOŞ, K-088) → argüman boyutu/şema doğrulaması → zorunlu audit yazımı
(K-089, hata çalıştırmayı KESER) → eşzamanlılık kotası. Argümanlar stdin ile
geçirilir (K-091, komut satırı/env DEĞİL — enjeksiyonu engeller), ortam
`Environment.Clear()` ile sıfırlanır. **Bilinçli sınır (K-086):** AgentPrism
dosya sistemi hapsi, ağ kısıtı, kaynak kotası SAĞLAMAZ — bunlar barındırma
ortamına bırakılmıştır; `PlatformIsolationAcknowledged` bayrağı olmadan
açılış hata verir.

## Ara

- Yorumlayıcı allow-list'in varsayılan olarak hâlâ BOŞ olduğunu (yeni bir
  varsayılan yorumlayıcı sessizce eklenmemiş).
- Argümanların HER çağrı yolunda stdin ile geçtiğini — bir kod yolunda
  argümanın komut satırına veya ortam değişkenine sızıp sızmadığını
  (komut enjeksiyonu riski).
- Eşzamanlılık kotasının paralel istekle (race condition) atlatılıp
  atlatılamayacağını — kota sayacının artırımı atomik mi.
- `PlatformIsolationAcknowledged` uyarısının README/config'te hâlâ göze
  çarpar ve doğru olduğunu — host bu bayrağı gördüğünde dosya
  sistemi/ağ izolasyonunun OLMADIĞINI gerçekten anlıyor mu (metin belirsizse
  bu bir dokümantasyon boşluğudur, kod kusuru değil ama yine de bildir).
- Audit store'un GERÇEKTEN her script çalıştırmadan ÖNCE mi yoksa SONRA mı
  yazıldığını — sıra yanlışsa denetimsiz bir çalıştırma penceresi açılır.
