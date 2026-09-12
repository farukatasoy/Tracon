# Extension Seam Matrisi

> `nuget-danismani` Adım 3'ün kaynak dosyasıdır. Skill **ne yapılacağını**
> söyler; bu dosya **neyin sorulacağını** söyler.
>
> Tabloyu her yayın turunda **yeniden** doldur. Bir önceki turun tablosu bir
> kanıt değil, bir başlangıç noktasıdır.

## Seam'i nasıl bulursun

Bir yüzey şu üç şeyin **hepsini** taşıyorsa seam'dir:

1. Public bir arayüz veya soyut tip — üçüncü taraf onu implemente edebilir
2. Bir registration yolu — DI'ye girer (`Add*`, `Use*`, options, factory)
3. Runtime onu **çağırır** — bir boru hattının içinde yaşar

Üçünden biri eksikse seam değildir; **hangisinin eksik olduğu** bir bulgudur.
Registration yolu olmayan public arayüz, "extension point" diye dokümante
edilmişse 🔴'dır.

```bash
grep -rn "public interface I" src/Tracon.Abstractions/ | wc -l
grep -rn "public static ITraconBuilder Add" src/ | sed 's/.*Add/Add/' | sort -u
```

## Yirmi iki sütun

Her seam için doldur. Boş bırakılan hücre bir **bulgu adayıdır** — "bilmiyorum"
cevabı, dokümantasyonun da bilmediği anlamına gelir.

| # | Boyut | Sorulacak soru |
|---|---|---|
| 1 | Registration API | `AddX<T>()` · `AddX(instance)` · `AddX(factory)` — üçü de var mı? Yoksa neden? |
| 2 | DI lifetime | Singleton mı scoped mı transient mi? **Belgeli mi?** |
| 3 | Lifetime anlamı | Singleton ise implementasyon `scoped` bağımlılığı nasıl alır? |
| 4 | Thread-safety | Aynı örnek eşzamanlı çağrılır mı? Sözleşme bunu **söylüyor** mu? |
| 5 | Per-run mutable state | Implementasyon alan tutabilir mi? Tutarsa ne bozulur? |
| 6 | Cancellation | Token akıyor mu? Yok sayılırsa ne olur? |
| 7 | Timeout | Gerçek wait cutoff var mı, yoksa yalnız cooperative token mı? |
| 8 | Dispose / kaynak sahipliği | Üretilen nesneyi **kim** dispose eder? Belgeli mi? |
| 9 | Kiracı semantiği | Kiracı kimliği seam'e ulaşıyor mu? Yalıtım kimin sorumluluğunda? |
| 10 | Cache semantiği | Sonuç cache'leniyor mu? **Fingerprint'e her davranışsal bağımlılık giriyor mu?** |
| 11 | Duplicate name | Aynı mantıksal adla iki kayıt: startup'ta mı runtime'da mı patlar? Yoksa sessiz mi? |
| 12 | Identity comparator | Ad karşılaştırması case-sensitive mi? Culture-invariant mi? |
| 13 | İzin verilen ad biçimi | Doğrulanıyor mu? Nerede — kayıtta mı kullanımda mı? |
| 14 | Hata normalizasyonu | Yabancı exception ne olur? Ham mesaj dışarı çıkar mı? |
| 15 | Retry semantiği | Retry var mı? Idempotency varsayılıyor mu? Sözleşme bunu söylüyor mu? |
| 16 | Idempotency | Aynı çağrı iki kez koşarsa ne olur? |
| 17 | Fallback | Desteklenmeyen bir yetenek istendiğinde `fail-closed` mı, sessiz düşüş mü? |
| 18 | Güvenlik sınırı sahipliği | Guard/authorization seam'in **içinde** mi dışında mı? |
| 19 | Observability | `span`/metric/log üretiliyor mu? Ad ve **cardinality** sözleşmenin parçası mı? |
| 20 | Contract testi | Reusable bir contract taban sınıfı var mı? Gerçek davranışı ölçüyor mu? |
| 21 | Sample | Yalnız `PackageReference` ile koşan dış bir sample var mı? |
| 22 | AOT / trimming | Seam AOT-safe mi? Reflection gerektiriyor mu? Beklenti belgeli mi? |

Sütun 19 için ek not: metric/`span` **adı** ve etiket kümesi de bir public
sözleşmedir. Yüksek cardinality'li bir etiket (kiracı kimliği, `run` kimliği)
tüketicinin metric sisteminde maliyet üretir; yayından sonra kaldırmak
tüketicinin dashboard'unu kırar.

## Yayın-inceleme sezgileri

Bunlar **kural değil, doğrulanacak hipotezlerdir**. Her biri bu depoda ya da
benzer bir kütüphanede en az bir kez gerçek bir kusur üretti. Yayın turunda
sırayla sor; cevabı **ölç**, varsayma.

| # | Hipotez | Nasıl ölçülür |
|---|---|---|
| 1 | Contract test paketi production `Core` bağımlılığı taşıyor | `.nuspec` `<dependencies>` — test paketi production graph'a girmemeli |
| 2 | Optional davranış sessiz `Skip` ile hiç koşmuyor | Contract suite'in koşan test sayısını **say**; `Skip` gerekçelerini oku |
| 3 | Yeni contract ailesi mevcut consumer'ları kırıyor | Eski aileyi kullanan sample'ı **yeni pakete karşı** derle |
| 4 | Dış sample gizlice source'a bağlanıyor | `ProjectReference` ve wildcard `VersionOverride` taraması |
| 5 | Source generator'ın ürettiği kodu başka bir generator **aynı pass'te göremiyor** | Üretilen dosyayı diskte oku; ikinci generator'ın çıktısında ara |
| 6 | Exact artifact davranışı kaynak ağacından farklı | Aynı senaryoyu hem `ProjectReference` hem packed tüketiciyle koş |
| 7 | Üretilen doküman ve paket metaverisi drift etti | `unzip -p <nupkg> <Id>.nuspec` ile `Description` ve `README`'yi karşılaştır |
| 8 | Ham üçüncü taraf exception public boundary'ye sızıyor | Fırlatan bir fake implementasyon kur; SSE · buffered HTTP · persisted error · MCP · OpenAI-uyumlu uçların **hepsini** oku |
| 9 | `fail-closed` iddiası test edilmemiş | Normalize edilemeyen bir değer üret; çıktının **boş** değil **reddedilmiş** olduğunu doğrula |
| 10 | `Timeout` gerçekte yalnız cancellation bütçesi | Token'ı **yok sayan** bir implementasyon yaz; çağrının gerçekten kesildiğini ölç |
| 11 | Kiracı credential capability'si örtük | Desteklemeyen bir provider'a kiracı credential'ı ver; invocation sayısının `0` olduğunu ölç |
| 12 | Singleton extension eşzamanlı çağrılıyor | Barrier'lı gerçek overlap testi — `Task.WhenAll` tek başına kanıt değil |
| 13 | Dönen koleksiyon çağıran tarafından mutate edilebiliyor | Dönüşten sonra mutate et; bir sonraki okumayı doğrula |
| 14 | Cache fingerprint eksik bağımlılık taşıyor | Davranışı değiştiren her girdiyi tek tek değiştir; cache'in **ıskaladığını** doğrula |
| 15 | Kiracı override/BYOK cache'i bypass etmiyor | İki kiracıyla aynı isteği koş; ikincisinin birincinin cevabını almadığını ölç |
| 16 | Kayıt ergonomisi seam'ler arasında tutarsız | Sütun 1 ve 2'yi seam'ler arasında **yan yana** oku |
| 17 | Duplicate mantıksal kimlik davranışı belirsiz | İki kayıt yap; startup mı runtime mı, hangisi kazanıyor — ölç |
| 18 | Dispose sahipliği belgesiz | XML dokümanında ara; yoksa 🟡, yanlışsa 🔴 |

Sezgi listesi bu depoya **hard-code edilmiş kural değildir**. Bir hipotez
ölçüldü ve yanlış çıktıysa bunu raporda yaz — "ölçüldü, temiz" bir sonraki
turun en ucuz bilgisidir.
