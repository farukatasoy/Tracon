namespace AgentPrism;

/// <summary>
/// Bir <see cref="IJobHandler"/>'in isi belirli bir sure sonra yeniden denemek
/// istedigini bildiren istisna.
/// </summary>
/// <remarks>
/// <para>
/// Siradan bir istisna isi hemen yeniden kiralanabilir hale getirir. Bir isleyici
/// geri adimli bekleme istiyorsa bunu firlatir; arka plan iscisi
/// <see cref="RetryAfter"/> degerini <see cref="IJobStore.ReleaseForRetryAsync"/>
/// cagrisina gecirir.
/// </para>
/// <para>
/// Deneme sayisi yine <see cref="JobRecord.Attempt"/> ile sinirlanir: bu istisna
/// isi sonsuza kadar canli tutmaz.
/// </para>
/// </remarks>
public sealed class JobRetryException : AgentPrismException
{
    /// <summary>
    /// <see cref="AgentPrismException.ErrorType"/> icin yazilan kararli deger.
    /// </summary>
    public const string JobRetryErrorType = "job_retry";

    /// <summary>Yeni bir yeniden deneme talebi olusturur.</summary>
    public JobRetryException()
    {
    }

    /// <summary>Yeni bir yeniden deneme talebi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    public JobRetryException(string message)
        : base(message)
    {
    }

    /// <summary>Yeni bir yeniden deneme talebi olusturur.</summary>
    /// <param name="message">Hata mesaji.</param>
    /// <param name="innerException">Asil hata.</param>
    public JobRetryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Bir sonraki denemeden once beklenecek sure. <see langword="null"/> ise
    /// is hemen yeniden kiralanabilir.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <inheritdoc />
    public override string ErrorType => JobRetryErrorType;
}
