namespace AgentPrism;

/// <summary>Tek bir skill script calistirmasinin sonucu.</summary>
public sealed record SkillScriptExecutionResult
{
    /// <summary>Surecin cikis kodu. Zaman asiminda <see langword="null"/>.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Standart cikti. Sinir asildiysa kirpilmistir.</summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>Standart hata akisi. Sinir asildiysa kirpilmistir.</summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>Cikti sinira takildi mi.</summary>
    public bool Truncated { get; init; }

    /// <summary>Sure asildi mi. Asildiysa surec agaci oldurulmustur.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Calistirmanin suresi.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Calistirma basariyla bitti mi.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
