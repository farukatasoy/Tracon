# SQL Server — Yerel Doğrulama (Apple Silicon)

> `sql-saglayicilari.md`dan ayrıldı (2026-08-07, bütçe aşımını gidermek için —
> aynı gerekçeyle `sqlite.md` de ayrılmıştı). Yalnızca bu makinede/Apple
> Silicon'da SQL Server testi koşturmaya çalışırken okunur.

## Çözüldü (2026-08-12, K-386): kök sebep Docker Desktop sürüm uyumsuzluğuydu

Aşağıdaki "hâlâ koşmuyor" bölümü 2026-08-07 tarihli teşhisin ARŞİVİDİR — kök
sebep o gün bulunamamıştı. 2026-08-12'de bulundu ve düzeltildi:

- **Kök sebep**: Kurulu Docker Desktop **4.29.0** (Şubat 2024) host macOS
  **26.5.1** için çok eskiydi. Ayar dosyası (`useVirtualizationFrameworkRosetta:
  true`) doğruydu, VM `--rosetta` bayrağıyla açılıyordu, HOST Rosetta (`oahd`
  süreci, `arch -x86_64 /usr/bin/true`) çalışıyordu — ama container İÇİ amd64
  yürütme her seferinde `exit 133` veriyordu. Karar verici kanıt: `log show
  --predicate 'eventMessage CONTAINS[c] "rosetta"'` son 20 dakikada TEK bir
  satır dönmedi — Docker'ın VM açılışı Rosetta kurulumunu hiç DENEMİYORDU.
  `brew info --cask docker` güncel sürümün 4.85.0+ olduğunu gösterdi; ~50
  sürümlük fark bu teşhisi doğruladı.
- **Düzeltme**: `brew install --cask docker` ile 4.86.0'a güncellendi.
  Güncelleme sonrası `docker run --rm --platform linux/amd64 busybox uname -m`
  `x86_64` döndü.
- **🚨 Cask'ın kendi hatası uygulamayı SİLDİ**: bu makinede
  `brew install --cask docker` "Unexpected method 'postflight_steps'..." ile
  hata verdi, ardından var olan `/Applications/Docker.app`'ı kaldırıp yeni
  sürümü taşımaya çalışırken `/usr/local/bin/docker-credential-osxkeychain`
  için `sudo` istedi — TTY olmadığı için `sudo` başarısız oldu ve rollback
  `/Applications/Docker.app`'ı TAMAMEN SİLİK bıraktı. Kurtarma: indirilen DMG
  Homebrew'un önbelleğinde sağlam kalır
  (`~/Library/Caches/Homebrew/downloads/*Docker.dmg`); `hdiutil attach` ile
  mount edip `cp -Rp "/Volumes/Docker/Docker.app" /Applications/Docker.app`
  ile elle kopyalamak yeterlidir (`ditto` bu DMG'de bazı ikili dosyalarda
  "No such file or directory" ile başarısız oldu, düz `cp -Rp` çalıştı).
  Kopyaladıktan sonra `codesign -dv /Applications/Docker.app` ile imzayı
  doğrula, `open -a Docker` ile aç.
- **Sonuç**: `SqlServerFixture.cs` HİÇ değiştirilmeden (aşağıdaki
  `azure-sql-edge` ikamesine hiç gerek kalmadan) gerçek
  `mcr.microsoft.com/mssql/server:2022-latest` ile **479/479** yeşil koştu.
- **Bu bulgu genelleşir**: Docker Desktop'ta Rosetta/VZ ile ilgili açıklanamayan
  bir hata görürsen ÖNCE kurulu sürümü `brew info --cask docker`'ın gösterdiği
  güncel sürümle karşılaştır — ayar dosyaları doğru görünse bile eski bir
  Docker Desktop sürümü host'un yeni bir macOS sürümüyle sessizce
  uyumsuzlaşabilir.

## Performans (2026-08-12, K-387/K-388/K-389/K-390)

Container gerçekten koşmaya başlayınca (yukarıdaki K-386) `AgentPrism.SqlServer.IntegrationTests`
en yavaş paket olarak kaldı — bu makinede `mssql/server` yalnızca `linux/amd64`
ve Apple Silicon'da Rosetta ile çalışıyor; ölçülen emülasyon cezası konteyner
içi aynı CPU döngüsü için **~9,2×** (arm64 2,55 sn, amd64 23,38 sn). CI
(`ubuntu-latest`/`windows-latest`) nativ x64 olduğu için bu ceza yalnız yerel
makineye özgü.

Sıra: K-387 (şema gerçekten bırakılıyor) → K-388 (round-trip birleşimi) →
**K-389/K-390** (migrasyon kilidi semaya kapsandı + sema test SINIFI başına
paylaşıldı, izolasyon `ResetDataAsync` ile). Tam çözüm koşumunda ölçülen sıra:
422,9 sn → ~300 sn → ~285,5 sn → **~31 sn** (izole kosum, 480 test). Aynı
K-389/K-390 değişikliği Postgres'i 112 sn'den ~14-15 sn'ye, SQLite'ı 44,8
sn'den ~6 sn'ye indirdi.

K-389'un migrasyon kilidini semaya kapsaması PostgreSQL'de ayrı bir kusuru
açığa çıkardı: `0024_vector.sql`'in `CREATE EXTENSION IF NOT EXISTS vector;`
ifadesi VERİTABANI genelinde paylaşılan bir katalog nesnesidir, semaya değil.
29 sınıf fixture'i eş zamanlı ilk kez migrate olunca bu benzersizlik ihlaline
(`pg_extension_name_index`) düştü — K-391'de hem genel bir yeniden deneme
(`MigrationRunner.ApplyOneAsync`, jitter'lı) hem de test tarafında uzantının
container başlar başlamaz bir kez önceden kurulması (`PostgresFixture.InitializeAsync`)
ile çözüldü.

## Arşiv (2026-08-07 teşhisi — artık geçerli değil)

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
çalıştırma anında bu çöküyor. **2026-08-12'de kök sebep bulundu: yukarıdaki
bölüme bakın.**

## `azure-sql-edge`: Rosetta yine bozulursa yedek teknik (K-317)

Bu makinede artık gerekli DEĞİL (yukarıdaki "Çözüldü" bölümüne bakın) ama
Docker Desktop/Rosetta başka bir makinede veya gelecekte yine bozulursa aynı
teknik geçerlidir. K-186 (Faz 23) bir kez başarılı oldu, Faz 25'te aynı teknik
tekrar denenip "artık HER ZAMAN çalışmayabilir" diye kayda geçmişti — kök sebep
hiç teşhis edilmemişti. 2026-08-07'de teşhis edildi ve kalıcı biçimde
düzeltildi:

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
              $"User Id={MsSqlBuilder.DefaultUsername};Password=" + MsSqlBuilder.DefaultPassword + ";" +
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

Bu makinede SQL Server sözleşme testlerini koşturman istenirse (2026-08-12
itibarıyla beklenen sonuç: doğrudan çalışır, `dotnet test
tests/AgentPrism.SqlServer.IntegrationTests -c Release`):

1. `docker run --rm --platform linux/amd64 busybox uname -m` ile hızlı bir ön
   kontrol yap. `x86_64` dönerse gerçek `mssql/server` çalışır, doğrudan testi
   koştur.
2. `rosetta error` ile başarısız olursa: önce kurulu Docker Desktop sürümünü
   (`grep -A1 CFBundleShortVersionString /Applications/Docker.app/Contents/Info.plist`)
   `brew info --cask docker`'ın gösterdiği güncel sürümle karşılaştır — büyük
   bir fark varsa `brew install --cask docker` ile güncelle (Docker Desktop'ı
   önce kapat: `osascript -e 'quit app "Docker"'`; cask'ın `postflight_steps`
   hatası + TTY'siz `sudo` isteği uygulamayı silebilir, kurtarma adımları
   yukarıdaki "Çözüldü" bölümünde).
3. Güncelleme sonrası da başarısızsa `azure-sql-edge` + yukarıdaki özel
   `IWaitUntil` ile geçici olarak `SqlServerFixture`'ı değiştir, testleri
   koştur, sonucu bu dosyaya ve `docs/KARARLAR.md`'ye kaydet, fixture'ı
   geri al.
