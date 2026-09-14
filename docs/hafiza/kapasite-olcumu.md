# Kapasite Ölçümü (`bench/capacity/`)

> Faz 166'da açıldı. Yük altında HTTP/SQL yolunu ölçen aparatın tuzakları.
> Aparatın **ne yaptığı** ve nasıl koşulduğu
> [`bench/capacity/README.md`](../../bench/capacity/README.md)'dedir; burası
> yalnız **bedeli ödenmiş** derslerdir.
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnız bu alana dokunurken okunur.
> Kardeşleri: aparatın MSBuild yalıtımı için
> [`build-ve-analyzer.md`](build-ve-analyzer.md), tanı susturma için
> [`analyzer-tanilari.md`](analyzer-tanilari.md), process altyapısı için
> [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).

## Ölçümün kendisi

- **🚨 Kapasite ölçümü bir KAPI DEĞİLDİR** (K-738). `kapi.py kapanis` onu
  çağırmaz; CI'da yalnız `smoke` profili, hızı değil DOĞRULUĞU kontrol etmek
  için koşar. Bir profili kapanışa eklemek her fazı saatler uzatır ve kapıların
  hiç koşulmamasıyla biter.
- **🚨 Kayan sürümle ölçüm yapılmaz.** `--surum` exact ister. Hangi baytların
  ölçüldüğünü söyleyemeyen bir rapor kanıt değildir; `*-*` ile koşan bir ölçüm
  makine üzerindeki herhangi bir paketi tüketmiş olabilir.
- **🚨 Koşum PAKETLERKEN çalışma ağacına DOKUNMA.** Temizlik kapısı `kapi.py`
  başlangıcında bir kez değil, `dotnet pack` sırasında **her paket için**
  koşar. Ölçüldü (2026-09-14): koşum başladıktan sonra tek bir `docs/` dosyası
  düzenlemek 17 paketin hepsini `TRACON0004` ile düşürdü ve üç saatlik bir
  ölçümü ilk iki dakikasında bitirdi. Pencere yalnız pack süresidir (~2 dk);
  koşumu başlat, pack bitene kadar bekle, sonra yazmaya devam et.
- **🚨 Kirli ağaçta üretilen sayı yayımlanamaz.** `dotnet pack`'in kendi kapısı
  kirli ağacı reddeder; aparat bunu sürümde `dirty` istemeye çevirir. Yayına
  giren her tablo temiz bir commit'ten üretilmiş olmalıdır.

## PostgreSQL

- **🚨 `pg_isready` TEK BAŞINA hazır demek değildir.** Resmî imaj önce yalnız
  UNIX soketinde dinleyen GEÇİCİ bir sunucu açar, init script'lerini koşar ve
  onu kapatır. O pencerede `pg_isready` başarı döner ve sonraki ilk sorgu
  `the database system is shutting down` alır — ölçüldü, koşum çöktü. Hazırlık
  **TCP üzerinden gerçek bir sorguyla** ve üst üste üç kez doğrulanır; geçici
  sunucu TCP'de hiç dinlemez.
- **`SUM(pg_total_relation_size(...))` `numeric` döner**, `bigint` değil.
  Npgsql onu `decimal` olarak verir ve `(long)` cast'i `InvalidCastException`
  atar. SQL'de `::bigint` yaz.
- **🚨 `pg_total_relation_size` farkı BLOAT'ı da sayar.** Aynı yük autovacuum
  pencerede çalıştıysa başka bayt verir. Üç önlem birlikte uygulanır ve üçü de
  raporlanır: hücre başına taze veritabanı, pencerenin İKİ yanında autovacuum
  durumu, ve ölçümün **drain bittikten sonra** alınması. Satır sayısı bloat'tan
  etkilenmez — bu yüzden satır ve bayt **birlikte** raporlanır; ayrıştıklarında
  sebep budur.

## Yalıtım ve seed

- **Hücre yalıtımı `schema` değil VERİTABANI sınırındadır** (Faz 166, Sapma 1).
  Gerekçe ölçüldü: `full` fixture'ı public store yolundan yazmak 10.000 run ×
  21 store çağrısıdır; her dolu hücrede tekrarlamak seed'i saatlerce kritik yola
  koyar. Şekil başına BİR şablon veritabanı kurulur, her hücre
  `CREATE DATABASE … TEMPLATE …` ile kendi kopyasını alır (dosya kopyası).
- **Seed BEYANI değil GERÇEK satır sayısı doğrulanır.** Yazdığından fazlasını
  iddia eden bir seed "dolu veritabanı" hücresini kurguya çevirir; orkestratör
  `COUNT(*)` ile karşılaştırır ve uyuşmazsa koşumu durdurur.

## Process

- **Hazırlık SATIRLA beklenir, süreyle değil.** Host `CAPACITY-HOST-READY`,
  worker `CAPACITY-WORKER-READY` yazar. Sabit `sleep` yüklü makinede kırılgandır
  ve açılış süresini ölçüm penceresine sokar (Faz 157'nin devir notu bunu
  açıkça ister).
- **🚨 Şema göçü AYRI bir çağrıdır** (`migrate` modu). Ölçülen host açılışta
  göç ederse şema kurulumu ilk hücrenin penceresine girer; `workers` profilinde
  N worker aynı göç kilidi için yarışır.
- **🚨 Worker ekseninde host'un KENDİ worker'ı kapatılır.** Açık kalırsa
  "1 worker" hücresi aslında iki worker olur ve tüm eksen kayar. Sürücü bunu
  profile GÜVENMEZ, çalışma anında `/capacity/settings`'ten okur; `true` ise
  hücre başlamadan `invalid` olur.
- **Hangi job'ı hangi process'in çalıştırdığı veritabanından okunamaz.**
  Scheduler job tamamlanınca `jobs.lease_owner`'ı `NULL`'lar. Kanıt,
  process'lerin kendi execution kaydıdır (process başına BİR dosya — birden çok
  process aynı dosyaya eklerken yük altında satırlar iç içe geçer ve yırtık bir
  satır kayıp bir execution'dan ayırt edilemez).
- **Konsol log'u ölçümün parçasıdır.** `Information` seviyesi senkron `stdout`
  yazar; yönlendirilmiş boruda bu, ölçülen şeyi `stdout`'a çevirir. Ölçülen
  host `Warning`'de koşar.

## Sayı okuma

- **Yavaşlık bir hücreyi `invalid` YAPMAZ.** `invalid` yalnız ölçümün
  güvenilmez olduğu hâldir: sayım hatası, veri kaybı, kiracı karışması, eksik
  zorunlu ölçüm, aynı job'ın iki worker'da ÖRTÜŞEN çalışması. Fazladan attempt
  meşrudur (K-641), örtüşme değildir.
- **Percentile'lar tekrarlar arasında ORTALANMAZ.** Örnekler birleştirilir ve
  percentile bir kez hesaplanır; iki p95'i toplayıp ikiye bölmenin dağılımsal
  bir anlamı yoktur. Örnek sayısı her percentile ile birlikte taşınır
  (p95 tabanı 100, p99 tabanı 1000).
- **Ölçülemeyen bir metrik sıfır YAZILMAZ**, `unavailable` + sebep yazılır.
  Sıfır "ölçüldü ve hiçti" diye okunur; bu başka ve yanlışlanamaz bir iddiadır.
