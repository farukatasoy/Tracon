# AgentPrism.Mcp

Uzak **Model Context Protocol** sunucularinin tool'larini AgentPrism kataloguna baglar.

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)
       .UsePostgreSql(connectionString)
       .UseMcp();
```

Sunucular kodda degil, **veritabaninda** tanimlidir; arayuzden veya
`PUT {prefix}/api/mcp-servers/{ad}` ucundan eklenir. Kesif arka planda yapilir ve
bulunan tool'lar kodda kayitli tool'larin yaninda listelenir.

## Guvenlik siniri

MCP sunucusu eklemek, **disaridan gelen tool tanimlarini kabul etmek** demektir ve
AgentPrism'in "tool'lar yalnizca kodda tanimlanir" kuralinin (K2) bilincli bir
istisnasidir. Su korumalarla gelir:

| Koruma | Nasil |
|--------|-------|
| Yalnizca uzak sunucu | Yalnizca `http`/`https`. **Stdio yoktur** — sunucuda surec baslatmak, arayuze erisen birinin sunucuda program calistirmasi demektir |
| Onay zorunlulugu | MCP tool'lari varsayilan olarak `RequiresApproval = true`; cagri kullanicinin onayini bekler |
| Ad ele gecirme yok | Kodda kayitli bir tool'un adini tasiyan MCP tool'u **yok sayilir**; kod her zaman kazanir |
| Sir sizmaz | Sunucu kaydi kimlik dogrulama **degerini** tasimaz, yalnizca degerin okunacagi yapilandirma anahtarinin **adini** tasir |
| Denetim izi | Her cagri kaynak sunucu adiyla `tool_invocations` tablosuna yazilir |
| Hacim siniri | Sunucu basina ust tool sayisi (`MaxToolsPerServer`, varsayilan 100) |

## Kimlik dogrulama

Sir veritabanina **yazilmaz**. Sunucu kaydinda yalnizca anahtarin adi durur:

```json
{
  "endpoint": "https://mcp.ornek.com/mcp",
  "authorizationConfigurationKey": "AgentPrism:Mcp:OrnekToken"
}
```

Deger calisma aninda `IConfiguration` uzerinden cozulur:

```bash
dotnet user-secrets set "AgentPrism:Mcp:OrnekToken" "Bearer ..."
```

Boylece veritabani yedegi, denetim izi ve arayuz yaniti hicbir zaman sir tasimaz.

## Tool adlari

Kesfedilen tool'lar `{sunucu}_{tool}` bicimiyle adlandirilir. Onek zorunludur: iki
farkli sunucuda ayni adli tool bulunmasi olagandir. Nokta **kullanilmaz** — OpenAI
ve uyumlu saglayicilar fonksiyon adlarinda yalnizca `[a-zA-Z0-9_-]` kabul eder.

## Ayarlar

| Ayar | Varsayilan | Ne yapar |
|------|-----------|----------|
| `Enabled` | `true` | Kesif acik mi |
| `RefreshInterval` | 5 dk | Tool listesi ne siklikta tazelenir |
| `ConnectionTimeout` | 30 sn | Baglanma ve listeleme ust suresi |
| `MaxToolsPerServer` | 100 | Sunucu basina ust tool sayisi |

Tazeleme normalde arka planda yapilir. Yeni eklenen bir sunucunun tool'larini hemen
gormek icin `POST {prefix}/api/mcp-servers/refresh`.

## Davranis

- Bir sunucuya **ulasilamamasi hata degildir**: o sunucunun tool'lari listeden duser,
  digerleri calismaya devam eder, bir uyari loglanir.
- Ilk kesif uygulama acilisini **bloklamaz**. Erisilemeyen bir MCP sunucusu
  uygulamayi baslatmaktan alikoymaz.
- Baglantilar tazeleme arasinda **ayakta tutulur**; yalnizca sunucu tanimi
  degistiginde yeniden kurulur. Degismeyen bir baglantiyi kapatmak, o sirada devam
  eden bir tool cagrisini kirardi.
- Tool'lar **kiraciya gore** cozulur: bir kiracinin sunucusundan gelen tool baska
  bir kiracida gorunmez.

## AOT

Bu paket **AOT uyumlu degildir**. MCP tool semalari calisma aninda cozulur ve
`ModelContextProtocol.Core` JSON serilestirmede yansima kullanir.
`AgentPrism.Abstractions`, `.Core`, `.PostgreSql` ve `.OpenAI` AOT uyumlu kalir.

Ayrinti: [`docs/06-GOZLEMLENEBILIRLIK.md`](https://github.com/farukatasoy/AgentPrism/blob/main/docs/06-GOZLEMLENEBILIRLIK.md)
