# SQL Server — Yerel Doğrulama (Apple Silicon)

> `sql-saglayicilari.md`dan ayrıldı (2026-08-07, bütçe aşımını gidermek için —
> aynı gerekçeyle `sqlite.md` de ayrılmıştı). Yalnızca bu makinede/Apple
> Silicon'da SQL Server testi koşturmaya çalışırken okunur.

## Gerçek `mssql/server` bu makinede hâlâ koşmuyor

`mcr.microsoft.com/mssql/server` yalnızca `linux/amd64`. Docker Desktop'ın
`useVirtualizationFrameworkRosetta: true` ayarı **açık olsa bile** (2026-08-07'de
`~/Library/Group Containers/group.com.docker/settings.json` içinde doğrulandı)
ve Docker Desktop **tam yeniden başlatıldıktan sonra bile** (`osascript -e
'quit app "Docker"'` + tüm `com.docker.*`/`Docker Desktop` süreçlerini
`pkill -9` ile temizleyip yeniden açmak dahil), `docker run --platform
linux/amd64 mcr.microsoft.com/mssql/server:2022-latest` container içinde
`rosetta error: Rosetta is only intended to run on Apple Silicon with a macOS
host using Virtualization.framework with Rosetta mode enabled` ile `exit 133`
veriyor. VM önyükleme kaydı (`~/Library/Containers/com.docker.docker/Data/log/vm/console.log`)
`Rosetta for Linux mounted/registered` satırlarını gösteriyor — yani VM
seviyesinde Rosetta gerçekten devreye giriyor, ama container'ın kendi amd64
çalıştırma anında bu çöküyor. Bu, Docker Desktop 4.29.0 ile macOS 26.5.1
arasında bir uyumsuzluk izlenimi veriyor; kesin teşhis Docker Desktop
güncellemesi veya destek talebi gerektirir — bu oturumda daha ileri
gidilmedi. **Gerçek doğrulama için hâlâ linux/amd64 bir makine veya CI
gerekir.**

## `azure-sql-edge` artık güvenilir bir yerel ikame (K-317)

K-186 (Faz 23) bir kez başarılı oldu, Faz 25'te aynı teknik tekrar denenip
"artık HER ZAMAN çalışmayabilir" diye kayda geçmişti — kök sebep hiç teşhis
edilmemişti. 2026-08-07'de teşhis edildi ve kalıcı biçimde düzeltildi:

- **Kök sebep**: `Testcontainers.MsSql` 4.13.0'ın `MsSqlBuilder.Init()`
  içindeki varsayılan `IWaitUntil` (decompile ile doğrulandı) container'ın
  İÇİNE `ExecAsync` ile `sqlcmd -C -Q "SELECT 1;"` çalıştırır
  (`MsSqlContainer.GetSqlCmdFilePathAsync`). `azure-sql-edge` imajı bu
  ikiliyi TAŞIMAZ. Container tamamen sağlıklı çalışsa bile hazır-olma
  denetimi asla `true` dönmez ve `StartAsync()` zaman aşımına uğrar.
- **Fixture'ın kendisi `sqlcmd`'ye hiç ihtiyaç duymuyor**: `SqlServerFixture`
  ve sözleşme testleri yalnızca ADO.NET (`Microsoft.Data.SqlClient`)
  kullanıyor, `ExecScriptAsync` çağrılmıyor. Yani `sqlcmd` yalnızca
  Testcontainers'ın kendi hazır-olma denetiminin bir iç detayı — atlanabilir.
- **Düzeltme**: `MsSqlBuilder(image)` için varsayılan yerine özel bir
  `IWaitUntil` verilir; bu, container'ın `Hostname`/`GetMappedPublicPort(1433)`
  bilgisinden gerçek bir `SqlConnection` kurup `SELECT 1` dener:

  ```csharp
  file sealed class SqlConnectionWaitUntil : IWaitUntil
  {
      public async Task<bool> UntilAsync(IContainer container)
      {
          var cs = $"Server={container.Hostname},{container.GetMappedPublicPort(MsSqlBuilder.MsSqlPort)};" +
              $"User Id={MsSqlBuilder.DefaultUsername};Password={MsSqlBuilder.DefaultPassword};" +
              "TrustServerCertificate=True;Connect Timeout=1";
          try
          {
              await using var connection = new SqlConnection(cs);
              await connection.OpenAsync().ConfigureAwait(false);
              await using var command = connection.CreateCommand();
              command.CommandText = "SELECT 1;";
              await command.ExecuteScalarAsync().ConfigureAwait(false);
              return true;
          }
          catch { return false; }
      }
  }

  // SqlServerFixture içinde:
  new MsSqlBuilder("mcr.microsoft.com/azure-sql-edge:latest")
      .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(new SqlConnectionWaitUntil()))
      .WithCleanUp(true)
      .Build();
  ```

- **Bu teknikle 431/431 sözleşme testi `azure-sql-edge` üzerinde yeşil koştu**
  (2026-08-07) ve dört gerçek üretim hatası bulundu ve düzeltildi:
  K-318 (dört migration dosyasında aynı toplu işlemde `ALTER TABLE ADD` +
  o sütunu kullanan `CREATE INDEX`) ve K-319 (`EvalCaseResult.Scores` boşken
  SQL Server'ın `ISJSON` kısıtını ihlal eden `"null"` yazımı).
- **Bu değişiklik `SqlServerFixture.cs`'e KALICI olarak yazılmaz.** CI ve
  "gerçek doğrulama" hedefi hâlâ `mcr.microsoft.com/mssql/server`dır; bu
  teknik yalnızca bu makinede YEREL bir doğrulama ihtiyacı doğduğunda
  geçici olarak uygulanır (fixture'ı değiştir, testleri koştur, sonra
  `git checkout` ile geri al — K-186'nın izlediği yöntemin aynısı, artık
  güvenilir).

## Tekrar dene rehberi

Bu makinede SQL Server sözleşme testlerini koşturman istenirse:

1. Önce gerçek `mssql/server`'ı dene (yukarıdaki `docker run --platform
   linux/amd64` komutuyla) — Docker Desktop güncellenmiş olabilir.
2. Başarısızsa `azure-sql-edge` + yukarıdaki özel `IWaitUntil` ile geçici
   olarak `SqlServerFixture`'ı değiştir, testleri koştur, sonucu bu dosyaya
   ve `docs/KARARLAR.md`'ye kaydet, fixture'ı geri al.
