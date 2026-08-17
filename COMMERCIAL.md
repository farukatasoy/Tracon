# Ticari Yol Haritası

Bu dosya, AgentPrism'in ticarileşme yönü hakkında açık bir taahhüttür. Amaç:
gelecekte bir ücretli katman eklendiğinde, bugün MIT lisansıyla kullanan
kimsenin sürpriz yaşamaması.

## Taahhüt

**Bugün [README.md](README.md)'deki "Paketler" tablosunda MIT olarak listelenen
her paket, sonsuza dek MIT kalır.** Zaten yayınlanmış hiçbir yetenek —
çok kiracılılık, audit log, canary rollback, evals, workflows, sağlayıcı
adaptörleri, gömülü arayüz — geriye dönük olarak ücretli bir lisansa taşınmaz.
Zaten yayınlanmış bir NuGet paket sürümünün lisansı da değişmez.

Bu taahhüt, listeye yeni bir paket eklenmesini engellemez. Aşağıdaki adaylar
**sadece yeni ve ayrı** bir pakette doğabilir; mevcut paketlerden hiçbir
kapsam çıkarılmaz.

## Ticari katman adayları (henüz yok, taahhüt değil)

Aşağıdakilerin hiçbiri bugün mevcut değildir. Bunlar yalnızca üzerinde
düşünülen yöndür, bir teslim tarihi taşımaz:

- SSO/OIDC federasyonu ve ekran bazlı RBAC — bugün yalnız tek bir
  `RequireAuthorization` policy var
- Uyumluluk/denetim dışa aktarımı (SOC2 kanıt paketi formatında)
- Çoklu ortam lisans yönetimi (dev/staging/prod ayrı anahtar)
- Yönetilen barındırma ("AgentPrism Cloud") — yalnız yukarıdakilere gerçek
  talep oluşursa değerlendirilir

## Sınır

- Self-hosted kullanım için bugün var olan hiçbir özellik, gelecekte bir
  lisans anahtarı gerektirmeyecek.
- Yeni bir ticari paket, mevcut `AgentPrism.*` paketlerinin API'sini veya
  davranışını değiştirmez — yalnız üstüne eklenir.
