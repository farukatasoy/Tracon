# AgentPrism.Sqlite

[AgentPrism](https://github.com/farukatasoy/AgentPrism) icin SQLite kalicilik katmani.

Tek dosyalik kurulum: demo, gomulu/kenar (edge) senaryolar ve bellek ici
depolarin otesinde gercek SQL davranisiyla test icin. Tablolar tuketicinin
kendi tablolariyla catismasin diye yapilandirilabilir bir **onek** tasir
(varsayilan `agentprism_`); SQLite'ta sema kavrami yoktur.

## Kurulum

```bash
dotnet add package AgentPrism.Sqlite
```

```csharp
builder.AddAgentPrism()
       .UseSqlite("Data Source=agentprism.db");
```

Bellekte, yalniz test icin:

```csharp
builder.AddAgentPrism()
       .UseSqlite("Data Source=:memory:");   // SINIR: baglanti kapanirsa veri gider
```

Ayarlarla:

```csharp
builder.AddAgentPrism()
       .UseSqlite(options =>
       {
           options.ConnectionString = "Data Source=agentprism.db";
           options.TablePrefix = "agentprism_";
           options.CommandTimeoutSeconds = 30;
           options.AutoApplyMigrations = true;
       });
```

## SQLite'ın gerçek sınırları

Bunlar gizlenmez; bu README'de ve `/api/meta` cikisinda bildirilir.

| Sınır | Sonuç |
|-------|-------|
| **Tek yazıcı** | Eşzamanlı yazma serileşir. Yüksek çalıştırma hacminde `run_events` yazımı darboğaz olur |
| WAL zorunlu | Bağlantı açılışında otomatik ayarlanır; kapatılamaz |
| `busy_timeout` | 5000 ms olarak ayarlanır |
| Tip sistemi zayıf | `datetimeoffset` yok, `uuid` yok, `decimal` yok — hepsi metin/sayı olarak kodlanır |
| Ağ yok | Tek süreçlidir; **çok örnekli dağıtımda kullanılamaz** |
| `SKIP LOCKED` yok | İş kuyruğu (`RunWorker`) tek işçiyle çalışır; çok örnekli olamaz |

> Bu sınırlar SQLite'ı kötü yapmaz; **yanlış yerde kullanmak** kötü yapar.

## Migration'lar

Gömülü `.sql` dosyaları uygulama başlarken otomatik uygulanır. Kilit
`sp_getapplock`/`pg_advisory_lock` karşılığı taşımaz — sidecar bir dosya
kilidiyle (`<veritabanı-dosyası>.agentprism-migration-lock`) korunur.
`:memory:` veritabanlarında kilit atlanır (başka bir süreç aynı bağlantıyı
paylaşamaz).

```csharp
options.AutoApplyMigrations = false;
```

Migration numaraları **sağlayıcı başınadır**; `AgentPrism.PostgreSql` ve
`AgentPrism.SqlServer` ile eşleşmez ve eşleşmesi gerekmez.

## AOT

Bu paketin AOT durumu **ölçülmedi**. `SQLitePCLRaw` yerel kütüphane taşır;
bu genellikle yayınlama (publish) davranışını etkiler. AOT gereken
kurulumlarda `AgentPrism.PostgreSql` kullanın.

## İki sağlayıcı birden

`UsePostgreSql()`, `UseSqlServer()` ve `UseSqlite()` aynı zincirde
çağrılırsa **son kayıt kazanır** ve açılışta uyarı loglanır. Bu bir
yapılandırma hatasıdır; yalnızca birini çağırın.
