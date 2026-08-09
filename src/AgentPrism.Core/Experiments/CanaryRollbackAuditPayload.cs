namespace AgentPrism;

/// <summary>
/// <c>experiment.auto_rollback</c> denetim kaydinin <c>After</c> govdesi.
/// </summary>
internal sealed record CanaryRollbackAuditPayload
{
    /// <summary>Geri almanin insan tarafindan okunabilir gerekcesi.</summary>
    public required string Reason { get; init; }

    /// <summary>Kanarya kolunun geri alma anindaki hata orani.</summary>
    public double? CanaryErrorRate { get; init; }

    /// <summary>Kontrol kolunun geri alma anindaki hata orani.</summary>
    public double? ControlErrorRate { get; init; }

    /// <summary>Kanarya kolunun geri alma anindaki ortalama puani.</summary>
    public double? CanaryAverageScore { get; init; }
}
