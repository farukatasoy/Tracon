namespace AgentPrism;

/// <summary>
/// MCP ve A2A dis yuzeylerinin paylastigi acilis denetimleri (Faz 50).
/// </summary>
/// <remarks>
/// Iki denetim de <c>MapAgentPrismMcpServer</c>/<c>MapAgentPrismA2A</c> cagrisi
/// aninda, uygulama daha ilk istegi almadan calisir. Amac K1'in ayni uygulamasi:
/// sessizce yarim calisan bir dis yuzey yerine acik bir acilis hatasi.
/// </remarks>
internal static class ExternalSurfaceGuard
{
    /// <summary>Bir agentin beyaz listede olup olmadigini soyler.</summary>
    /// <param name="agentName">Denetlenecek agent adi.</param>
    /// <param name="exposedAgents">Beyaz liste.</param>
    /// <param name="exposeAll">Tum agent'lar acik mi.</param>
    /// <returns>Agent disa aciksa <see langword="true"/>.</returns>
    public static bool IsExposed(string agentName, ICollection<string> exposedAgents, bool exposeAll)
        => exposeAll || exposedAgents.Contains(agentName, StringComparer.Ordinal);

    /// <summary>
    /// <c>AllowRemoteAccess</c> acikken bir dis yuzeyin acilmasini engeller.
    /// </summary>
    /// <param name="allowRemoteAccess"><see cref="AgentPrismEndpointOptions.AllowRemoteAccess"/> degeri.</param>
    /// <param name="protocol">Acilan dis yuzeyin adi.</param>
    /// <exception cref="InvalidOperationException">Uzak erisim acikken cagrilmissa.</exception>
    /// <remarks>
    /// Tek statik bearer token, loopback disina acilmis bir agent yuzeyini
    /// korumaya yetmez (bolum 50.4). Kalici cozum kiraci bazli API anahtaridir
    /// (F-56); o gelene kadar bu iki ozellik birlikte acilamaz.
    /// </remarks>
    public static void EnsureRemoteAccessNotCombined(bool allowRemoteAccess, string protocol)
    {
        if (!allowRemoteAccess)
        {
            return;
        }

        throw new InvalidOperationException(
            $"AllowRemoteAccess acikken {protocol} disa acilamaz. Tek statik bearer token, " +
            "loopback disina acilmis bir agent yuzeyini korumaya yetmez. Kiraci bazli API " +
            "anahtarlari (F-56) eklendiginde bu kisit kaldirilabilir.");
    }

    /// <summary>
    /// Disa acilacak agent'lardan hicbirinin onay gerektiren bir tool tasimadigini
    /// dogrular.
    /// </summary>
    /// <param name="descriptors">Katalogdaki agent ozetleri.</param>
    /// <param name="exposedAgents">Beyaz liste.</param>
    /// <param name="exposeAll">Tum agent'lar acik mi.</param>
    /// <param name="toolRegistry">Onay bayragini tasiyan tool defteri.</param>
    /// <param name="protocol">Acilan dis yuzeyin adi.</param>
    /// <exception cref="InvalidOperationException">
    /// Disa acik bir agent onay gerektiren bir tool tasiyorsa.
    /// </exception>
    /// <remarks>
    /// K-103'un ayni sinirini acilista zorlar: dis cagiran bir insan degildir ve
    /// onay isteğine cevap veremez. Sessizce onaysiz calistirmak kabul edilemez.
    /// </remarks>
    public static void EnsureNoApprovalRequiredTools(
        IReadOnlyList<AgentDescriptor> descriptors,
        ICollection<string> exposedAgents,
        bool exposeAll,
        IToolRegistry toolRegistry,
        string protocol)
    {
        var approvalRequiredToolNames = toolRegistry.List()
            .Where(static tool => tool.RequiresApproval)
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        if (approvalRequiredToolNames.Count == 0)
        {
            return;
        }

        foreach (var descriptor in descriptors)
        {
            if (!IsExposed(descriptor.Name, exposedAgents, exposeAll))
            {
                continue;
            }

            var offending = descriptor.ToolNames.Where(approvalRequiredToolNames.Contains).ToList();

            if (offending.Count == 0)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"'{descriptor.Name}' agent'i {protocol} uzerinden disa acilamaz: " +
                $"'{string.Join(", ", offending)}' tool'lari kullanici onayi istiyor. " +
                "Dis cagiran bir agent degildir ve onay isteğine cevap veremez. Bu agent'i " +
                "beyaz listeden cikarin veya onay gerektirmeyen tool'larla sinirlayin.");
        }
    }
}
