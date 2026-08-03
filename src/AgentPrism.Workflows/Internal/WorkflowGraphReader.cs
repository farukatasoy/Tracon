using Microsoft.Agents.AI.Workflows;

namespace AgentPrism;

/// <summary>
/// Derlenmis bir <see cref="Workflow"/>'dan arayuzun cizebilecegi grafi cikarir.
/// </summary>
/// <remarks>
/// <para>
/// Cikarim Microsoft Agent Framework'un uc yansitma metoduna dayanir:
/// <c>ReflectExecutors()</c> dugumleri, <c>ReflectEdges()</c> kenarlari,
/// <c>ReflectPorts()</c> dis istek portlarini verir. <c>WorkflowVisualizer</c>
/// ayrica hazir bir Mermaid metni uretir; o metin arayuzde <em>cizilmez</em>,
/// yalnizca disari aktarilir - tarayicida Mermaid render etmek ~100 KB gzip
/// eder ve bundle butcesi 250 KB'dir (K-002).
/// </para>
/// <para>
/// Dugum kimlikleri calistirma olaylarindaki executor kimlikleriyle
/// <strong>birebir</strong> ayni tutulur; arayuz dugumleri canli olarak bu
/// sayede renklendirir. Etiket kisaltilir ama kimlik hicbir zaman degistirilmez.
/// </para>
/// </remarks>
internal static class WorkflowGraphReader
{
    /// <summary>Grafi cikarir.</summary>
    /// <param name="name">Workflow adi.</param>
    /// <param name="workflow">Derlenmis graf.</param>
    /// <returns>Arayuze gonderilecek graf.</returns>
    public static WorkflowGraph Read(string name, Workflow workflow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workflow);

        var executors = workflow.ReflectExecutors();
        var ports = workflow.ReflectPorts();

        var nodes = new List<WorkflowGraphNode>(executors.Count);

        foreach (var (id, binding) in executors)
        {
            var isPort = ports.ContainsKey(id);
            var agentName = AgentNameOf(binding, id);

            nodes.Add(new WorkflowGraphNode
            {
                Id = id,
                Label = agentName ?? Shorten(id),
                Kind = Classify(binding, isPort, agentName),
                AgentName = agentName,
                ExecutorType = binding.ExecutorType.Name,
            });
        }

        var edges = new List<WorkflowGraphEdge>();

        foreach (var (source, connections) in workflow.ReflectEdges())
        {
            foreach (var edge in connections)
            {
                foreach (var sink in edge.Connection.SinkIds)
                {
                    edges.Add(new WorkflowGraphEdge
                    {
                        From = source,
                        To = sink,
                        Kind = Map(edge.Kind),
                    });
                }
            }
        }

        return new WorkflowGraph
        {
            Name = name,
            StartExecutorId = workflow.StartExecutorId,
            Nodes = [.. nodes.OrderBy(static node => node.Id, StringComparer.Ordinal)],
            Edges = [.. edges.OrderBy(static edge => edge.From, StringComparer.Ordinal)
                             .ThenBy(static edge => edge.To, StringComparer.Ordinal)],
            Mermaid = WorkflowVisualizer.ToMermaidString(workflow),
        };
    }

    /// <summary>Bir dugumun temsil ettigi agent'in adini cikarir.</summary>
    /// <remarks>
    /// Bagli agent'i <see cref="AIAgentBinding"/> dogrudan tasir. Handoff deseni
    /// agent'i kendi executor'una sarar ve baglama tipinden okunamaz; o durumda
    /// kimlik <c>{ad}_{kimlik}</c> biciminden ayristirilir. Kimlik bolumu
    /// AgentPrism tarafindan uretildigi icin (32 onaltilik karakter) ayristirma
    /// tahmine dayanmaz.
    /// </remarks>
    private static string? AgentNameOf(ExecutorBinding binding, string id)
    {
        if (binding is AIAgentBinding agent)
        {
            return agent.Agent.Name;
        }

        // 🚨 Bilesik kimlikler ayristirilmaz. Olculdu (Faz 16): `Concurrent`
        // deseni her agent icin bir `Batcher/{ad}_{kimlik}` dugumu ekler ve
        // sondaki kimlik parcasi ayni bicimdedir; egik cizgi denetlenmeseydi
        // birlestirici dugumler agent sanilir ve graf iki kat agent gosterirdi.
        if (id.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        var separator = id.LastIndexOf('_');

        if (separator <= 0 || id.Length - separator - 1 != 32)
        {
            return null;
        }

        for (var index = separator + 1; index < id.Length; index++)
        {
            if (!Uri.IsHexDigit(id[index]))
            {
                return null;
            }
        }

        return id[..separator];
    }

    private static WorkflowNodeKind Classify(ExecutorBinding binding, bool isPort, string? agentName)
    {
        if (isPort || binding is RequestPortBinding)
        {
            return WorkflowNodeKind.RequestPort;
        }

        if (agentName is not null)
        {
            return WorkflowNodeKind.Agent;
        }

        // Cikti dugumleri hazir desenlerin sonuna eklenir ve grafin sonucunu
        // toplar; arayuzde ayri bir bicimle cizilir.
        return binding.ExecutorType.Name.Contains("Output", StringComparison.Ordinal)
            ? WorkflowNodeKind.Output
            : WorkflowNodeKind.Orchestration;
    }

    private static WorkflowEdgeKind Map(EdgeKind kind) => kind switch
    {
        EdgeKind.FanOut => WorkflowEdgeKind.FanOut,
        EdgeKind.FanIn => WorkflowEdgeKind.FanIn,
        _ => WorkflowEdgeKind.Direct,
    };

    /// <summary>Uzun bir executor kimligini okunabilir bir etikete kisaltir.</summary>
    private static string Shorten(string id)
    {
        var slash = id.LastIndexOf('/');

        return slash >= 0 && slash < id.Length - 1 ? id[(slash + 1)..] : id;
    }
}
