namespace AgentPrism.Testing;

/// <summary>Kaydedilmis bir calistirma uzerinde iddialar.</summary>
/// <remarks>
/// Her iddia karsilanmazsa <see cref="AgentPrismAssertionException"/> firlatir ve
/// mesaji **beklenen ve bulunan** degeri yazar. Akisli calistirmalar da kapsanir:
/// <c>run_events</c> akisli yolda da dolar, ayri bir tip gerekmez.
/// </remarks>
public sealed class RunAssertions
{
    internal RunAssertions(
        RunRecord record,
        IReadOnlyList<RunEvent> events,
        IReadOnlyList<ToolInvocationRecord> toolInvocations)
    {
        Record = record;
        Events = events;
        ToolInvocations = toolInvocations;
    }

    /// <summary>Calistirma kaydi.</summary>
    public RunRecord Record { get; }

    /// <summary>Calistirmanin olay akisi, sira numarasina gore.</summary>
    public IReadOnlyList<RunEvent> Events { get; }

    /// <summary>Calistirma sirasinda yapilan tool cagrilari.</summary>
    public IReadOnlyList<ToolInvocationRecord> ToolInvocations { get; }

    /// <summary>Calistirmanin basariyla tamamlandigini dogrular.</summary>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="AgentPrismAssertionException">Durum <see cref="RunStatus.Completed"/> degilse.</exception>
    public RunAssertions ShouldHaveCompleted()
    {
        if (Record.Status != RunStatus.Completed)
        {
            throw new AgentPrismAssertionException(
                $"Calistirmanin durumu 'Completed' olmasi beklenirdi ama '{Record.Status}' bulundu.");
        }

        return this;
    }

    /// <summary>Calistirmanin hata ile sonlandigini dogrular.</summary>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="AgentPrismAssertionException">Durum <see cref="RunStatus.Failed"/> degilse.</exception>
    public RunAssertions ShouldHaveFailed()
    {
        if (Record.Status != RunStatus.Failed)
        {
            throw new AgentPrismAssertionException(
                $"Calistirmanin durumu 'Failed' olmasi beklenirdi ama '{Record.Status}' bulundu.");
        }

        return this;
    }

    /// <summary>Calistirmanin belirli bir hata tipiyle sonlandigini dogrular.</summary>
    /// <param name="errorType"><see cref="AgentPrismException.ErrorType"/> ile eslesmesi beklenen deger.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="AgentPrismAssertionException">
    /// Durum <see cref="RunStatus.Failed"/> degilse veya hata tipi eslesmiyorsa.
    /// </exception>
    public RunAssertions ShouldHaveFailedWith(string errorType)
    {
        ShouldHaveFailed();

        var actual = Record.Error?.Type;

        if (!string.Equals(actual, errorType, StringComparison.Ordinal))
        {
            throw new AgentPrismAssertionException(
                $"Hata tipinin '{errorType}' olmasi beklenirdi ama '{actual}' bulundu.");
        }

        return this;
    }

    /// <summary>Belirli bir tool'un cagrildigini dogrular.</summary>
    /// <param name="toolName">Tool adi.</param>
    /// <param name="times">Verilirse tam olarak bu sayida cagrilmis olmasi beklenir.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="AgentPrismAssertionException">Tool hic cagrilmadiysa veya sayi eslesmiyorsa.</exception>
    public RunAssertions ShouldHaveCalledTool(string toolName, int? times = null)
    {
        var count = ToolInvocations.Count(invocation => string.Equals(invocation.ToolName, toolName, StringComparison.Ordinal));

        if (count == 0)
        {
            throw new AgentPrismAssertionException(
                $"'{toolName}' tool'unun en az bir kez cagrilmasi beklenirdi ama hic cagrilmadi.");
        }

        if (times is { } expected && count != expected)
        {
            throw new AgentPrismAssertionException(
                $"'{toolName}' tool'unun {expected} kez cagrilmasi beklenirdi ama {count} kez cagrildi.");
        }

        return this;
    }

    /// <summary>Belirli bir tool'un hic cagrilmadigini dogrular.</summary>
    /// <param name="toolName">Tool adi.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="AgentPrismAssertionException">Tool en az bir kez cagrildiysa.</exception>
    public RunAssertions ShouldNotHaveCalledTool(string toolName)
    {
        var count = ToolInvocations.Count(invocation => string.Equals(invocation.ToolName, toolName, StringComparison.Ordinal));

        if (count > 0)
        {
            throw new AgentPrismAssertionException(
                $"'{toolName}' tool'unun hic cagrilmamasi beklenirdi ama {count} kez cagrildi.");
        }

        return this;
    }

    /// <summary>Calistirmanin uretilen metninin belirli bir alt metni icerdigini dogrular.</summary>
    /// <param name="text">Aranan alt metin.</param>
    /// <returns>Zincirin devami.</returns>
    /// <exception cref="AgentPrismAssertionException">Alt metin bulunamazsa.</exception>
    /// <remarks>
    /// <see cref="RunEventType.MessageCompleted"/> varsa (akissiz calistirma)
    /// o kullanilir; yoksa (akisli calistirma, ornegin HTTP <c>/run</c> ucu)
    /// <see cref="RunEventType.MessageDelta"/> parcalari birlestirilir. Ikisi
    /// AYNI calistirmada birlikte kullanilmaz — akissiz yolda ayrica
    /// parcalari da toplamak metni MUKERRER sayardi.
    /// </remarks>
    public RunAssertions ShouldHaveOutputContaining(string text)
    {
        var completed = Events
            .Where(static runEvent => runEvent.Type == RunEventType.MessageCompleted)
            .Select(static runEvent => runEvent.Text ?? string.Empty)
            .ToList();

        var output = completed.Count > 0
            ? string.Concat(completed)
            : string.Concat(Events
                .Where(static runEvent => runEvent.Type == RunEventType.MessageDelta)
                .Select(static runEvent => runEvent.Text ?? string.Empty));

        if (!output.Contains(text, StringComparison.Ordinal))
        {
            throw new AgentPrismAssertionException(
                $"Ciktinin '{text}' icermesi beklenirdi ama bulunan cikti: '{output}'.");
        }

        return this;
    }
}
