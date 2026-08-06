namespace AgentPrism;

/// <summary>
/// Agent cagri grafigini kaydetme aninda denetler.
/// </summary>
/// <remarks>
/// <para>
/// Denetim derinlik oncelikli arama ile yapilir ve uc hatayi ayirir: bilinmeyen
/// ad, kendi kendini cagirma, dolayli dongu. Ucu de kaydetme anini kirar
/// (<c>400 Bad Request</c>); calisma anina birakilirsa kullanici hatayi ancak
/// agent'i calistirdiginda ve okunmasi zor bir mesajla gorur.
/// </para>
/// <para>
/// <strong>Statik denetim tek basina yeterli degildir.</strong> Kod tarafinda
/// fabrika ile kaydedilmis bir agent bildirimsel bir tanim tasimaz ve grafikte
/// yaprak gorunur; gercekte ise baska agent'lari cagiriyor olabilir. Ikinci
/// savunma hatti calisma anindaki derinlik sayacidir
/// (<see cref="AgentRunBudget.MaxDepth"/>).
/// </para>
/// </remarks>
public static class AgentCallGraph
{
    /// <summary>
    /// Bir tanimin cagri grafigini denetler.
    /// </summary>
    /// <param name="agentName">Denetlenen agent'in adi.</param>
    /// <param name="callableAgentNames">Bu agent'in cagirmak istedigi agent adlari.</param>
    /// <param name="descriptors">Katalogdaki tum agent ozetleri.</param>
    /// <returns>
    /// Sorun varsa kullaniciya gosterilebilir aciklama; grafik gecerliyse
    /// <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    public static string? Validate(
        string agentName,
        IReadOnlyList<string> callableAgentNames,
        IReadOnlyList<AgentDescriptor> descriptors)
        => ValidateDetailed(agentName, callableAgentNames, descriptors)?.Message;

    /// <summary>
    /// Bir tanimin cagri grafigini denetler ve makine tarafindan okunabilir bir
    /// kod tasiyan sonuc dondurur.
    /// </summary>
    /// <param name="agentName">Denetlenen agent'in adi.</param>
    /// <param name="callableAgentNames">Bu agent'in cagirmak istedigi agent adlari.</param>
    /// <param name="descriptors">Katalogdaki tum agent ozetleri.</param>
    /// <returns>
    /// Sorun varsa kod ve aciklama; grafik gecerliyse <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <remarks>
    /// <see cref="Validate"/> ile <strong>ayni denetimi</strong> yapar; F-60'in
    /// dogrulama ucu kodu (<c>unknown_agent</c>/<c>cycle</c>) buradan alir,
    /// kaydetme anindaki <c>400</c> yaniti ise yalnizca <see cref="AgentCallGraphProblem.Message"/>'i kullanir.
    /// </remarks>
    public static AgentCallGraphProblem? ValidateDetailed(
        string agentName,
        IReadOnlyList<string> callableAgentNames,
        IReadOnlyList<AgentDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(callableAgentNames);
        ArgumentNullException.ThrowIfNull(descriptors);

        if (callableAgentNames.Count == 0)
        {
            return null;
        }

        // Denetlenen tanim henuz kaydedilmedigi icin katalogdaki hali eski
        // olabilir. Grafik, tanimin YENI hali ile kurulur; aksi halde yeni
        // eklenen bir kenarin dongu yaratip yaratmadigi hic gorulmezdi.
        var edges = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            known.Add(descriptor.Name);
            edges[descriptor.Name] = descriptor.CallableAgentNames;
        }

        known.Add(agentName);
        edges[agentName] = callableAgentNames;

        foreach (var target in callableAgentNames)
        {
            if (string.Equals(target, agentName, StringComparison.Ordinal))
            {
                return new AgentCallGraphProblem(
                    "cycle",
                    $"'{agentName}' kendisini cagiramaz. Bir agent'in kendisini cagirmasi " +
                    "sonsuz ozyinelemedir ve derinlik sayaci dolana kadar maliyet uretir.");
            }

            if (!known.Contains(target))
            {
                return new AgentCallGraphProblem(
                    "unknown_agent",
                    $"'{agentName}' agent'i '{target}' adli bir agent'i cagirmak istiyor ancak " +
                    "boyle bir agent katalogda yok. Once o agent'i olusturun.");
            }
        }

        return FindCycle(agentName, edges) is { } cycle
            ? new AgentCallGraphProblem(
                "cycle",
                $"Cagri grafiginde dongu var: {string.Join(" -> ", cycle)}. " +
                "Dongulu bir grafik, calistirmanin derinlik sinirina carpana kadar surmesine yol acar.")
            : null;
    }

    /// <summary>
    /// Verilen dugumden baslayarak grafikte bir dongu arar.
    /// </summary>
    /// <returns>Bulunan dongunun dugum sirasi; dongu yoksa <see langword="null"/>.</returns>
    /// <remarks>
    /// Ozyineleme yerine acik yigin kullanilir: cagri grafigi kullanici verisidir
    /// ve derinligi sinirsizdir; ozyinelemeli bir gezinti yeterince uzun bir
    /// zincirde <c>StackOverflowException</c> ile sureci oldururdu.
    /// </remarks>
    private static List<string>? FindCycle(string start, Dictionary<string, IReadOnlyList<string>> edges)
    {
        var path = new List<string>();
        var onPath = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<Frame>();

        stack.Push(new Frame(start, 0));
        path.Add(start);
        onPath.Add(start);

        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            var children = edges.TryGetValue(frame.Node, out var list) ? list : [];

            if (frame.Index >= children.Count)
            {
                onPath.Remove(frame.Node);
                visited.Add(frame.Node);

                if (path.Count > 0)
                {
                    path.RemoveAt(path.Count - 1);
                }

                continue;
            }

            stack.Push(frame with { Index = frame.Index + 1 });

            var child = children[frame.Index];

            if (onPath.Contains(child))
            {
                var cycle = new List<string>(path.Count + 1);
                cycle.AddRange(path);
                cycle.Add(child);

                return cycle;
            }

            if (visited.Contains(child))
            {
                continue;
            }

            stack.Push(new Frame(child, 0));
            path.Add(child);
            onPath.Add(child);
        }

        return null;
    }

    private readonly record struct Frame(string Node, int Index);
}

/// <summary>Bir cagri grafigi denetiminin makine tarafindan okunabilir sonucu.</summary>
/// <param name="Code">Kararli kod: <c>unknown_agent</c> veya <c>cycle</c>.</param>
/// <param name="Message">Insan tarafindan okunabilir aciklama.</param>
public readonly record struct AgentCallGraphProblem(string Code, string Message);
