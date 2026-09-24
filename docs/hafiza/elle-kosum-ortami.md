# Örnek Uygulamayı ELLE Koşma Tuzakları

> `samples/Tracon.Api`'yi bir insanın (ya da agent'ın) elle ayağa kaldırdığı
> her durumun tuzakları: manuel kabul turu, bir kusuru ampirik yeniden üretme,
> bir seam'i canlı gösterme. **Test koşumu değildir** — `dotnet test` tuzakları
> [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md)'dedir.
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnızca bu alana dokunurken
> okunur. Kaynak: 2026-09-16 manuel kabul turu ve kapanışı (22 aile, 1.859
> case); her madde o turda **bedel ödetti**.

## Uygulamayı başlatma

- **🚨 `--contentRoot` verilmezse `appsettings.json` HİÇ okunmaz.** Derlenmiş
  DLL doğrudan koşulurken kabuğun CWD'si content root sanılır. Belirti
  yanıltıcıdır: `GET /api/models` **boş liste** döner ve Development'ta
  `UseOpenAICompatible` "Endpoint is required" ile **açılışta** patlar. Doğru
  tarif:

  ```bash
  dotnet build samples/Tracon.Api -c Release
  ASPNETCORE_ENVIRONMENT=Development \
    dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll \
    --contentRoot "$PWD/artifacts/bin/Tracon.Api/release" \
    --urls http://127.0.0.1:5199
  ```

- **🚨 `user-secrets` yalnız Development'ta yüklenir.**
  `ASPNETCORE_ENVIRONMENT=Development` verilmezse sağlayıcı anahtarları
  görünmez ve agent **sessizce** `echo` sağlayıcısına düşer — yanıt modelden
  değil echo'dan gelir ve case yanlış sebeple yeşil görünür.

- **`dotnet run` iki şeritte sebepsiz "Application is shutting down" verdi.**
  Derlenmiş DLL'i doğrudan çalıştırmak bu belirsizliği ortadan kaldırır.

- **Nokta ya da tire taşıyan ayar ortam değişkeni olamaz** (zsh reddeder).
  Komut satırını kullan: `-- "--Tracon:Pricing:openai:gpt-5.4-mini:Input=0.25"`.
  Çift alt çizgi biçimi (`Tracon__Demo__RunAuthorization__Mode=deny-all`)
  noktasız anahtarlar için çalışır.

- **`timeout` macOS'ta yoktur** (çıkış 127). Süreci arka planda koş, çıkış
  kodunu dosyaya yaz.

## Yanıtı okuma

- **Alanlar kökte değil:** `usage.totalTokens` · `response.messages[0].contents[0].text`
  · sağlıkta `providerName`. Kökte arayan bir `jq` sessizce `null` görür.
- **`GET /api/runs/{id}/events` SSE döner, JSON değil.** Ham `grep` çok satırlı
  `data:` gövdesini böler; önce çerçeveyi birleştir.
- **Sağlayıcı hatasının ayrıntısı yanıtta DEĞİL günlüktedir** — `SafeErrorText`
  kasıtlı olarak sabit döndürür (`upstream_error`). Sebebi arıyorsan uygulama
  günlüğüne bak.
- **Eşlenmemiş bir yola GET dışı her metot `405` alır** (`404` değil): konsolu
  sunan host'ta SPA yedek rotası her yolu yalnız GET için eşler. Bir ucun
  GERÇEKTEN kapalı olduğunu kanıtlarken **hiç var olmamış** bir yolu karşı
  kontrol olarak koş — ikisi aynı cevabı veriyorsa rota yoktur (`MT-SEC-181`).

## Süreç-içi durum: yeniden başlatmadan temizlenmez

- **`QuotaEnforcer._firedThresholds` süreç-içidir**; SQL ile temizlenmez,
  uygulama **yeniden başlatılmalıdır**. Küme yalnız açık dönemin anahtarını
  tutar; kapanan dönem bir sonraki kayıtta düşer. Yani bu kural **aynı
  dönem** içindeki sıfırlama için geçerlidir.
- **Devre kesici de süreç-içidir**; onu sınayan case ayrı bir örnekte koşulur.

## Bayrak çiftleri ve demo kancaları

- **Kiracı başlığı İKİ bayrak ister:** `Tracon:Tenancy:Enabled=true` **ve**
  `AllowHeaderResolution=true`. İkisi de varsayılan kapalıdır ve kapalıyken
  başlık **sessizce** yok sayılır — ret değil, görmezden gelme.
- **Bir seam'i göstermek için `Program.cs`'i GEÇİCİ düzenleme** (K-834): örnek
  uygulamanın kalıcı demo kancaları vardır ve liste büyür —
  `Tracon:Demo:Roles:Enabled` · `Tracon:Demo:RunAuthorization:Mode` (sekiz
  kural) · `Tracon:Demo:ToolAuthorization:Mode` ·
  `Tracon:Demo:MapOpenAIConversations` · `Tracon:Demo:SuppressRegistrations` ·
  `Tracon:Demo:RequireRolePolicies` · `Tracon:Demo:ToolApprovalPolicy`.
  Bir ön koşul "şunu geçici ekle" diyorsa **önce kancayı ara**.
- **Model adı `gpt-5.4-mini`.** Rastgele bir OpenAI modeli `403
  model_not_found` verir. **Azure kimliği yoktur**; Azure isteyen case
  `⏭ Atlandı` kalır ve bu bir kusur değildir.

## `secret` hijyeni

- **`dotnet user-secrets list` ASLA filtresiz koşulmaz** — her zaman `grep`'le.
  Turda üç ayrı olayda (filtresiz `list`, `ps eww`, ortam hata ayıklaması) beş
  sağlayıcı anahtarı düz metne çıktı ve hepsinin döndürülmesi gerekti.

## Ölçüm disiplini — turun en pahalı üç dersi

- **🚨 Bir "ortamda bunun yolu yok" gerekçesi bir ÖLÇÜM değil bir
  HİPOTEZDİR.** Turun altı gerekçesi kapanışta denendi ve **altısı da**
  çürüdü: rol kancası zaten vardı, engel tarayıcı kilidiydi, kısıtlı rol
  kurulabildi, üç milyon satır `generate_series` ile üretildi, repo dışı host
  yerel feed'den kuruldu. Kapanış oturumu her gerekçeyi **önce dener**.
- **🚨 KARŞI KONTROL olmadan boş bir sonuç yanlış sebeple yeşil görünür.**
  "Sayaç boş" hem "kural çalışmadı" hem "hiç çağrılmadı" olabilir; ikinci bir
  ölçüm ret sebebini ayrıştırır (turda yedi case bu yüzden yeniden koşuldu).
- **🚨 YANLIŞ TAŞIMA yanlış sonucu doğru sandırır.** `401` her zaman
  yetkilendirme reddi değildir: ses ucunda token WebSocket **subprotocol**'üyle
  gider (`Sec-WebSocket-Protocol: tracon.voice.v1, tracon.token.<token>`).
  Aynı sınıf iki kez daha çıktı — ek uçları kökte `api/attachments`, karar ucu
  `approvals/{id}/decide`.

## Makineye runtime ekleme

- **🚨 `dotnet-install.sh --runtime` global köke `--skip-non-versioned-files`
  olmadan koşarsa `dotnet` muxer'ını YERİNDE ezer ve macOS onu öldürür.**
  Ölçüldü (2026-09-24): net8 runtime'ı `/usr/local/share/dotnet`'e eklendi;
  script sürümsüz dosyaları (`dotnet`, `LICENSE.txt`, `ThirdPartyNotices.txt`)
  8.0.31'inkilerle değiştirdi. `codesign -v` geçti ama her `dotnet` çağrısı
  çıkış 137 (`killed`) verdi: çekirdek imzayı eski inode için önbelleğe almıştı.
  Doğru komut:
  `sudo bash dotnet-install.sh --runtime dotnet --channel 8.0 --install-dir /usr/local/share/dotnet --no-path --skip-non-versioned-files`
  (aynısı `--runtime aspnetcore` ile).
- **Onarım yeni inode ister:** imzalı bir 10.x muxer'ı yanına kopyala, sonra
  `mv` ile değiştir (`sudo cp ~/.dotnet/dotnet …/dotnet.new && sudo mv -f
  …/dotnet.new …/dotnet`). Yerinde `cp` aynı kilitlenmeyi yeniden üretir.

