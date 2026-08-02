using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Bir calistirma boyunca baglam sikistirmasinin (ozetleme) urettigi token
/// kullanimini toplar.
/// </summary>
/// <remarks>
/// Ozetleme cagrisi agent'in kendi <c>AgentResponse</c>'undan tamamen ayri
/// bir yan-kanal cagrisidir; token'lari normal akisa hic girmez. Bu toplayici
/// <see cref="AgentPrismRunContext"/> uzerinden erisilir ve
/// <see cref="RunRecordingAgent"/> calistirma sonunda degerini nihai
/// <see cref="RunUsage"/>'a katar — boylece hem maliyet raporu hem agac
/// butcesi ozetleme maliyetini gorur.
/// </remarks>
internal sealed class CompactionUsageAccumulator
{
    private long _inputTokens;
    private long _outputTokens;
    private long _totalTokens;

    /// <summary>Bir ozetleme cagrisinin kullanimini toplama ekler.</summary>
    /// <param name="usage">Cagrinin kullanim detaylari. <see langword="null"/> ise yok sayilir.</param>
    public void Add(UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        if (usage.InputTokenCount is { } input)
        {
            Interlocked.Add(ref _inputTokens, input);
        }

        if (usage.OutputTokenCount is { } output)
        {
            Interlocked.Add(ref _outputTokens, output);
        }

        if (usage.TotalTokenCount is { } total)
        {
            Interlocked.Add(ref _totalTokens, total);
        }
    }

    /// <summary>Toplanan kullanimi bir <see cref="RunUsage"/>'a cevirir.</summary>
    /// <returns>Hicbir kullanim toplanmadiysa <see langword="null"/>.</returns>
    public RunUsage? ToRunUsage()
    {
        var input = Interlocked.Read(ref _inputTokens);
        var output = Interlocked.Read(ref _outputTokens);
        var total = Interlocked.Read(ref _totalTokens);

        if (input == 0 && output == 0 && total == 0)
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = input == 0 ? null : input,
            OutputTokens = output == 0 ? null : output,
            TotalTokens = total == 0 ? null : total,
        };
    }
}
