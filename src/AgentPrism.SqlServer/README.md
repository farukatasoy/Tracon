# AgentPrism.SqlServer

[AgentPrism](https://github.com/farukatasoy/AgentPrism) icin SQL Server kalicilik katmani.

Agent tanimlari, oturumlar, konusmalar, calistirmalar, olaylar, workflow'lar, is
kuyrugu, degerlendirme, kota ve webhook kayitlari ayri bir `agentprism`
semasinda saklanir. Tuketicinin `dbo` semasina dokunulmaz.

## Kurulum

```bash
dotnet add package AgentPrism.SqlServer
```

```csharp
builder.AddAgentPrism()
       .UseSqlServer(builder.Configuration.GetConnectionString("AgentPrism")!);
```

Ayarlarla:

```csharp
builder.AddAgentPrism()
       .UseSqlServer(options =>
       {
           options.ConnectionString = "...";   // sir: user-secrets veya ortam degiskeni
           options.SchemaName = "agentprism";
           options.CommandTimeoutSeconds = 30;
           options.AutoApplyMigrations = true;
       });
```

> **Baglanti dizesi bir sirdir ve dosyaya yazilmaz.** `dotnet user-secrets`,
> ortam degiskeni veya bir sir yoneticisi kullanin.

## Desteklenen surumler

| Ortam | Durum |
|-------|-------|
| SQL Server 2019+ | Desteklenir, CI'da test edilir (2022 imaji) |
| Azure SQL Database | Desteklenir, **CI'da test edilmez** — container ile ayaga kaldirilamaz |
| SQL Server 2017 ve oncesi | Desteklenmez |

## Migration'lar

Gomulu `.sql` dosyalari uygulama baslarken otomatik uygulanir. `sp_getapplock`
ile korunur: birden fazla replika ayni anda baslarsa yalnizca biri uygular.

Uretimde otomatik uygulamayi kapatip `MigrationRunner`'i ayri bir dagitim
adiminda calistirabilirsiniz:

```csharp
options.AutoApplyMigrations = false;
```

Migration numaralari **saglayici basinadir**; `AgentPrism.PostgreSql` ile
eslesmez ve eslesmesi gerekmez.

## AOT

Bu paket **AOT uyumlu degildir**: `Microsoft.Data.SqlClient` kirpma icin
isaretli degildir. AOT gereken kurulumlarda `AgentPrism.PostgreSql` kullanin.

## Iki saglayici birden

`UsePostgreSql()` ve `UseSqlServer()` ayni zincirde cagrilirsa **son kayit
kazanir** ve acilista uyari loglanir. Bu bir yapilandirma hatasidir; yalnizca
birini cagirin.
