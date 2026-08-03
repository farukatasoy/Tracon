namespace AgentPrism;

/// <summary>
/// Bir workflow tanimin yapisal gecerliligini denetler.
/// </summary>
/// <remarks>
/// <para>
/// Denetim <c>AgentPrism.Core</c> icindedir cunku <strong>iki tuketicisi</strong>
/// vardir: HTTP katmani kayit aninda <c>400</c> uretir, workflow derleyicisi
/// calistirma aninda istisna atar. Iki yerde ayri ayri yazilsaydi biri
/// degisip digeri kalirdi.
/// </para>
/// <para>
/// Burada yalnizca <em>yapisal</em> kurallar denetlenir: ad, sayi, tekrar,
/// desen-alan uyumu. Agent'larin katalogda gercekten var olup olmadigi
/// derleme aninda denetlenir; katalog kayit ile calistirma arasinda degisebilir
/// ve o denetimi kayit anina tasimak yanlis bir guvence verirdi.
/// </para>
/// </remarks>
public static class WorkflowDefinitionValidator
{
    /// <summary>Tanimi denetler.</summary>
    /// <param name="definition">Denetlenecek tanim.</param>
    /// <returns>
    /// Kullaniciya gosterilebilir hata metni; tanim gecerliyse <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> <see langword="null"/> ise.</exception>
    public static string? Validate(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            return "Workflow tanimin 'name' alani zorunludur.";
        }

        if (!Enum.IsDefined(definition.Kind))
        {
            return $"'{definition.Name}' workflow'u bilinmeyen bir desen kullaniyor: '{definition.Kind}'.";
        }

        if (definition.AgentNames.Count == 0)
        {
            return $"'{definition.Name}' workflow'u hicbir agent icermiyor. " +
                   "'agentNames' en az bir ad tasimalidir.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var agentName in definition.AgentNames)
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return $"'{definition.Name}' workflow'unun agent listesinde bos bir ad var.";
            }

            // Tekrar eden ad bilerek reddedilir. Microsoft Agent Framework
            // executor kimliklerini agent ORNEGINDEN turetir; ayni adin iki kez
            // gecmesi, arayuzde ve olay akisinda ayirt edilemeyen iki dugum
            // uretirdi. Bir rol tekrarlaniyorsa ikinci bir agent tanimlanmalidir.
            if (!seen.Add(agentName))
            {
                return $"'{definition.Name}' workflow'unda '{agentName}' agent'i birden fazla kez geciyor. " +
                       "Bir workflow'un agent listesi tekrarsiz olmalidir; ayni rol iki kez gerekiyorsa " +
                       "ikinci bir agent tanimlayin.";
            }
        }

        if (definition.Kind is WorkflowKind.Concurrent or WorkflowKind.Handoff or WorkflowKind.GroupChat &&
            definition.AgentNames.Count < 2)
        {
            return $"'{definition.Name}' workflow'u '{definition.Kind}' desenini kullaniyor ve en az iki agent " +
                   $"ister; listede {definition.AgentNames.Count} ad var.";
        }

        if (definition.Kind == WorkflowKind.Magentic)
        {
            if (string.IsNullOrWhiteSpace(definition.ManagerAgentName))
            {
                return $"'{definition.Name}' workflow'u 'Magentic' desenini kullaniyor ve 'managerAgentName' " +
                       "zorunludur. Yonetici agent plani kurar, ilerlemeyi izler ve gerektiginde yeniden planlar.";
            }

            if (seen.Contains(definition.ManagerAgentName))
            {
                return $"'{definition.Name}' workflow'unda '{definition.ManagerAgentName}' hem yonetici hem " +
                       "katilimci olarak geciyor. Yonetici, katilimcilari yonlendirir; kendini yonlendiremez.";
            }
        }

        // GroupChat'in yoneticisi bir AGENT DEGILDIR: sirayi dagitan kod
        // tarafindaki round-robin yoneticisidir. Alan verilmisse kullanici bir
        // sey bekliyor demektir ve sessizce yok saymak yaniltir.
        else if (!string.IsNullOrWhiteSpace(definition.ManagerAgentName))
        {
            return $"'{definition.Name}' workflow'u '{definition.Kind}' deseninde 'managerAgentName' kullanmaz. " +
                   "Bu alan yalnizca 'Magentic' desenine aittir; 'GroupChat' sirayi kod tarafindaki " +
                   "round-robin yoneticisiyle dagitir.";
        }

        if (definition.Kind != WorkflowKind.Handoff && !string.IsNullOrWhiteSpace(definition.HandoffInstructions))
        {
            return $"'{definition.Name}' workflow'u '{definition.Kind}' deseninde 'handoffInstructions' " +
                   "kullanmaz. Bu alan yalnizca 'Handoff' desenine aittir.";
        }

        // Plan onayi yalnizca Magentic'in kavramidir: yonetici agent'in kurdugu
        // plani insana gosterir. Diger desenlerde plan diye bir sey yoktur ve
        // alani sessizce yok saymak, kullanicinin bekledigi onay adiminin hic
        // olusmadigini gizlerdi - managerAgentName ile ayni kural.
        if (definition.RequirePlanApproval && definition.Kind != WorkflowKind.Magentic)
        {
            return $"'{definition.Name}' workflow'u '{definition.Kind}' deseninde 'requirePlanApproval' " +
                   "kullanmaz. Plan onayi yalnizca 'Magentic' desenine aittir; plani kuran yonetici " +
                   "agent yalnizca o desende bulunur.";
        }

        if (definition.MaxIterations is <= 0)
        {
            return $"'{definition.Name}' workflow'unun 'maxIterations' degeri pozitif olmalidir.";
        }

        return null;
    }

    /// <summary>Tanimi denetler ve gecersizse istisna atar.</summary>
    /// <param name="definition">Denetlenecek tanim.</param>
    /// <exception cref="AgentPrismException">Tanim gecersizse.</exception>
    public static void Require(WorkflowDefinition definition)
    {
        if (Validate(definition) is { } message)
        {
            throw new AgentPrismException(message);
        }
    }
}
