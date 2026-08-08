namespace AgentPrism;

/// <summary>
/// SQL semasi hazir olana kadar arka plan servislerini bekleten kapi.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Neden var:</strong> migration'lari uygulayan baslangic servisi bir
/// <c>IHostedService</c>'tir ve <c>StartAsync</c>'inde migration'lari TAM bekler.
/// Ama <c>BackgroundService</c> taban sinifinin <c>StartAsync</c>'i
/// <c>ExecuteAsync</c>'i beklemeden doner. <c>IHostedService</c>'ler
/// <em>kayit sirasinda</em> baslatildigi icin, zincirde <c>.UseMcp()</c>
/// <c>.UseSqlite()</c>'tan once cagrilirsa arka plan servisinin ilk SQL denemesi
/// migration bitmeden calisir ve "no such table" verir. Olculdu (Faz 42).
/// </para>
/// <para>
/// Kapi <strong>kayit sirasindan bagimsizdir</strong>: bekleyen taraf sirayi
/// bilmez, yalnizca hazir sinyalini bekler. Sirayi zorlamak kirilgan olurdu —
/// zinciri tuketici yazar ve her siralamayi dayatamayiz.
/// </para>
/// <para>
/// Hicbir SQL kalicilik saglayicisi kayitli degilse (bellek ici depolar) kapi
/// <em>kendiliginden</em> aciktir; aksi hâlde bellek ici kurulumda arka plan
/// servisleri sonsuza dek beklerdi.
/// </para>
/// </remarks>
public sealed class SchemaReadyGate
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IEnumerable<SqlPersistenceRegistrationMarker> _registrations;

    /// <summary>Yeni bir kapi olusturur.</summary>
    /// <param name="registrations">
    /// Kayitli SQL kalicilik saglayicilari. Bos ise kapi hicbir zaman kapanmaz.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="registrations"/> <see langword="null"/> ise.
    /// </exception>
    public SchemaReadyGate(IEnumerable<SqlPersistenceRegistrationMarker> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _registrations = registrations;
    }

    /// <summary>Sema hazir mi.</summary>
    public bool IsReady => _ready.Task.IsCompleted;

    /// <summary>
    /// Semayi hazir isaretler. Migration baslangic servisi cagirir.
    /// </summary>
    /// <remarks>
    /// Birden fazla cagri zararsizdir. <c>AutoApplyMigrations</c> kapaliyken de
    /// cagrilir: o durumda semanin hazir olmasi tuketicinin sorumlulugundadir ve
    /// arka plan servislerini beklemekte tutmanin bir faydasi yoktur.
    /// </remarks>
    public void MarkReady() => _ready.TrySetResult();

    /// <summary>
    /// Sema hazir olana kadar bekler.
    /// </summary>
    /// <param name="cancellationToken">Bekleme iptali.</param>
    /// <returns>Sema hazir oldugunda tamamlanan gorev.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> iptal edilirse. Migration basarisiz
    /// olursa barindirici zaten kapanir ve bekleyen servis bu yoldan cikar.
    /// </exception>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        // Kayitlar servis saglayici kurulduktan SONRA okunur; zincirin tamami
        // o ana kadar calismis olur. Kapi acilana kadar her cagride denetlenir,
        // sonrasinda tamamlanmis gorev dogrudan doner.
        if (!_ready.Task.IsCompleted && !_registrations.Any())
        {
            _ready.TrySetResult();
        }

        return _ready.Task.WaitAsync(cancellationToken);
    }
}
