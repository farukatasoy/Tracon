using System.Globalization;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Microsoft Agent Framework workflow olaylarini AgentPrism olay taslaklarina cevirir.
/// </summary>
/// <remarks>
/// 🚨 <strong>Dal sirasi onemlidir.</strong> <c>AgentResponseEvent</c> ve
/// <c>AgentResponseUpdateEvent</c>, <c>WorkflowOutputEvent</c>'ten
/// <em>turer</em> (Faz 15'te olculdu). Genel dal once yazilirsa agent yanitlari
/// "workflow cikti uretti" diye siniflanir ve gercek cikti kaybolur.
/// </remarks>
internal static class WorkflowEventMapper
{
    /// <summary>Bir MAF olayini AgentPrism olay taslagina cevirir.</summary>
    /// <param name="workflowEvent">Cevrilecek olay.</param>
    /// <returns>Cevrim sonucu.</returns>
    public static WorkflowEventMapping Map(WorkflowEvent workflowEvent)
    {
        ArgumentNullException.ThrowIfNull(workflowEvent);

        return workflowEvent switch
        {
            WorkflowStartedEvent => new RunEventDraft(RunEventType.WorkflowStarted),

            // Agent yanitlari WorkflowOutputEvent'ten TUREDIGI icin once eslenir.
            AgentResponseUpdateEvent update => new RunEventDraft(RunEventType.MessageDelta)
            {
                Text = update.Update?.Text,
                ToolName = update.ExecutorId,
            },

            // Akisli calistirmada guncelleme olaylari zaten metni tasir; tam
            // yanit olayi ayrica yazilirsa ayni metin akista iki kez gorunur.
            AgentResponseEvent => WorkflowEventMapping.Skipped,

            SuperStepStartedEvent started => new RunEventDraft(RunEventType.SuperStepStarted)
            {
                Text = started.StepNumber.ToString(CultureInfo.InvariantCulture),
                Payload = Join(started.StartInfo?.SendingExecutors),
            },

            SuperStepCompletedEvent completed => new RunEventDraft(RunEventType.SuperStepCompleted)
            {
                Text = completed.StepNumber.ToString(CultureInfo.InvariantCulture),
                Payload = DescribeCompletion(completed),
            },

            ExecutorFailedEvent failed => new RunEventDraft(RunEventType.ExecutorFailed)
            {
                Text = failed.ExecutorId,
                Payload = failed.Data?.Message,
            },

            ExecutorInvokedEvent invoked => new RunEventDraft(RunEventType.ExecutorInvoked)
            {
                Text = invoked.ExecutorId,
            },

            ExecutorCompletedEvent completed => new RunEventDraft(RunEventType.ExecutorCompleted)
            {
                Text = completed.ExecutorId,
            },

            RequestInfoEvent request => new RunEventDraft(RunEventType.WorkflowRequest)
            {
                Text = request.Request.RequestId,
            },

            WorkflowErrorEvent error => new RunEventDraft(RunEventType.ExecutorFailed)
            {
                Text = error.Exception?.GetType().Name,
                Payload = error.Exception?.Message,
            },

            WorkflowWarningEvent warning => new RunEventDraft(RunEventType.SuperStepCompleted)
            {
                Text = "warning",
                Payload = warning.Data?.ToString(),
            },

            WorkflowOutputEvent output => new RunEventDraft(RunEventType.WorkflowOutput)
            {
                Text = DescribeOutput(output),
            },

            _ => WorkflowEventMapping.Unknown,
        };
    }

    /// <summary>Bir cikti olayindaki metni cikarir.</summary>
    /// <param name="output">Cikti olayi.</param>
    /// <returns>Okunabilir metin; cikarilamiyorsa tip adi.</returns>
    /// <remarks>
    /// Hazir desenlerin tamami <c>List&lt;ChatMessage&gt;</c> uretir (Faz 15'te
    /// olculdu). Kodda tanimli serbest bir graf baska bir tip dondurebilir; o
    /// durumda yalnizca tip adi yazilir - bilinmeyen bir yuku <c>ToString()</c>
    /// ile olay tablosuna dokmek, tabloyu sisirirdi.
    /// </remarks>
    public static string? DescribeOutput(WorkflowOutputEvent output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var data = output.Data;

        return data switch
        {
            null => null,
            string text => text,
            IEnumerable<ChatMessage> messages => string.Join(
                Environment.NewLine,
                messages
                    .Where(static message => !string.IsNullOrWhiteSpace(message.Text))
                    .Select(static message => message.Text)),
            ChatMessage message => message.Text,
            _ => data.GetType().Name,
        };
    }

    private static string DescribeCompletion(SuperStepCompletedEvent completed)
    {
        var activated = Join(completed.CompletionInfo?.ActivatedExecutors);
        var checkpoint = completed.CompletionInfo?.Checkpoint?.CheckpointId;

        return checkpoint is null ? activated : $"{activated} · checkpoint={checkpoint}";
    }

    private static string Join(IEnumerable<string>? values)
        => values is null
            ? string.Empty
            : string.Join(", ", values.OrderBy(static value => value, StringComparer.Ordinal));
}

/// <summary>Bir workflow olayinin cevrim sonucu.</summary>
/// <param name="Draft">Yazilacak olay taslagi. Olay atlandiysa <see langword="null"/>.</param>
/// <param name="IsKnown">
/// Olay tipi taniniyor mu. <see langword="false"/> olan bir olay <strong>sessizce
/// dusurulmez</strong>, cagiran tarafindan loglanir: Microsoft Agent Framework
/// yeni bir olay tipi ekledigi gun bunun fark edilmemesi, hata ayiklamayi
/// imkansizlastirirdi.
/// </param>
internal readonly record struct WorkflowEventMapping(RunEventDraft? Draft, bool IsKnown)
{
    /// <summary>Taninan ama olay akisina yazilmayan bir olay.</summary>
    public static WorkflowEventMapping Skipped { get; } = new(null, IsKnown: true);

    /// <summary>Taninmayan bir olay.</summary>
    public static WorkflowEventMapping Unknown { get; } = new(null, IsKnown: false);

    /// <summary>Bir taslagi taninan bir cevrim sonucuna donusturur.</summary>
    /// <param name="draft">Olay taslagi.</param>
    public static implicit operator WorkflowEventMapping(RunEventDraft draft) => new(draft, IsKnown: true);
}
