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
            return "The workflow definition's 'name' field is required.";
        }

        if (!Enum.IsDefined(definition.Kind))
        {
            return $"Workflow '{definition.Name}' uses an unknown pattern: '{definition.Kind}'.";
        }

        if (definition.AgentNames.Count == 0)
        {
            return $"Workflow '{definition.Name}' has no agents. " +
                   "'agentNames' must carry at least one name.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var agentName in definition.AgentNames)
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return $"Workflow '{definition.Name}' has an empty name in its agent list.";
            }

            // Tekrar eden ad bilerek reddedilir. Microsoft Agent Framework
            // executor kimliklerini agent ORNEGINDEN turetir; ayni adin iki kez
            // gecmesi, arayuzde ve olay akisinda ayirt edilemeyen iki dugum
            // uretirdi. Bir rol tekrarlaniyorsa ikinci bir agent tanimlanmalidir.
            if (!seen.Add(agentName))
            {
                return $"Agent '{agentName}' appears more than once in workflow '{definition.Name}'. " +
                       "A workflow's agent list must have no duplicates; define a second agent if the " +
                       "same role is needed twice.";
            }
        }

        if (definition.Kind is WorkflowKind.Concurrent or WorkflowKind.Handoff or WorkflowKind.GroupChat &&
            definition.AgentNames.Count < 2)
        {
            return $"Workflow '{definition.Name}' uses the '{definition.Kind}' pattern, which requires " +
                   $"at least two agents; the list has {definition.AgentNames.Count}.";
        }

        if (definition.Kind == WorkflowKind.Magentic)
        {
            if (string.IsNullOrWhiteSpace(definition.ManagerAgentName))
            {
                return $"Workflow '{definition.Name}' uses the 'Magentic' pattern, and 'managerAgentName' " +
                       "is required. The manager agent builds the plan, tracks progress, and replans as needed.";
            }

            if (seen.Contains(definition.ManagerAgentName))
            {
                return $"In workflow '{definition.Name}', '{definition.ManagerAgentName}' appears as both " +
                       "manager and participant. The manager directs participants; it cannot direct itself.";
            }
        }

        // GroupChat'in yoneticisi bir AGENT DEGILDIR: sirayi dagitan kod
        // tarafindaki round-robin yoneticisidir. Alan verilmisse kullanici bir
        // sey bekliyor demektir ve sessizce yok saymak yaniltir.
        else if (!string.IsNullOrWhiteSpace(definition.ManagerAgentName))
        {
            return $"Workflow '{definition.Name}' does not use 'managerAgentName' in the " +
                   $"'{definition.Kind}' pattern. This field belongs only to the 'Magentic' pattern; " +
                   "'GroupChat' distributes turn order with a round-robin manager on the code side.";
        }

        if (definition.Kind != WorkflowKind.Handoff && !string.IsNullOrWhiteSpace(definition.HandoffInstructions))
        {
            return $"Workflow '{definition.Name}' does not use 'handoffInstructions' in the " +
                   $"'{definition.Kind}' pattern. This field belongs only to the 'Handoff' pattern.";
        }

        // Plan onayi yalnizca Magentic'in kavramidir: yonetici agent'in kurdugu
        // plani insana gosterir. Diger desenlerde plan diye bir sey yoktur ve
        // alani sessizce yok saymak, kullanicinin bekledigi onay adiminin hic
        // olusmadigini gizlerdi - managerAgentName ile ayni kural.
        if (definition.RequirePlanApproval && definition.Kind != WorkflowKind.Magentic)
        {
            return $"Workflow '{definition.Name}' does not use 'requirePlanApproval' in the " +
                   $"'{definition.Kind}' pattern. Plan approval belongs only to the 'Magentic' pattern; " +
                   "the manager agent that builds a plan exists only in that pattern.";
        }

        if (definition.MaxIterations is <= 0)
        {
            return $"Workflow '{definition.Name}''s 'maxIterations' value must be positive.";
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
