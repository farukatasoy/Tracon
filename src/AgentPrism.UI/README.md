# AgentPrism.UI

Gomulu yonetim arayuzu.

React 19 + TypeScript ile yazilmis tek sayfa uygulama. Vite ile derlenir ve assembly
icine **Brotli sikistirilmis** gomulur. Tuketici projede hicbir JavaScript bagimliligi
olusmaz; `node_modules` klasoru gerekmez.

## Kurulum

```bash
dotnet add package AgentPrism.UI --prerelease
```

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)
       .UseUI();

app.MapAgentPrism("/agentprism");
```

Ayri bir esleme cagrisi yoktur. `MapAgentPrism` kaydi bulur ve arayuzu ayni onek
altina baglar; onek tek yerde yazilir.

## Ekranlar

| Ekran | Icerik |
|-------|--------|
| Agents | Katalog (kod / veritabani), tanim editoru, surum gecmisi, geri alma |
| Playground | Akisli sohbet; tool cagrilari argumanlari ve sonuclariyla kart halinde |
| Sessions | Oturum listesi, sohbet gecmisi, ham durum, silme |
| Runs | Calistirma listesi, ozet, olay olay zaman cizelgesi |
| Tools | Kayitli tool'lar ve JSON semalari |
| Models | Saglayicilar, modeller, yetenek bayraklari |
| Settings | Surum, onek, kimlik yontemi, aktif depolar, tema |

## Notlar

- Arayuz herhangi bir onek altinda calisir (`/agentprism`, `/panel`, ...) ve onegi
  calisma aninda ogrenir
- Acik ve koyu tema; varsayilan isletim sistemi tercihidir
- Tool'lar yalnizca kodda tanimlanir. Arayuzden agent olusturulabilir, tool **kodu**
  yazilamaz — bu bir guvenlik sinirdir
- Arayuz kabugu bearer token katmanindan muaftir; loopback kisiti ve authorization
  policy uygulanir. Gerekce: tarayici bir betik istegine `Authorization` basligi
  ekleyemez
- JavaScript butcesi: 250 KB gzip (derleme kapisi). Su anki boyut ~88 KB

## Baglanti

- Depo ve tam dokumantasyon: <https://github.com/farukatasoy/AgentPrism>
- Mimari: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)
- Arayuz fazi: [docs/05-AGENTPRISM-UI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/05-AGENTPRISM-UI.md)

Lisans: MIT
