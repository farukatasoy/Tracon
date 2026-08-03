namespace AgentPrism;

/// <summary>
/// Bir workflow'un derlenmis grafi: dugumler, kenarlar ve dis istek portlari.
/// </summary>
/// <remarks>
/// <para>
/// Graf <strong>tanimdan degil, derlenmis workflow'dan</strong> cikarilir.
/// Sebep olculdur: hazir desenler kullanicinin yazmadigi executor'lar ekler
/// (<c>OutputMessages</c>, <c>Batcher/*</c>, <c>ConcurrentEnd</c>,
/// <c>HandoffStart</c>, <c>GroupChatHost</c>, <c>MagenticOrchestrator</c>).
/// Tanimdan cizilen bir graf bu dugumleri gostermez ve calistirma sirasinda
/// gelen <c>ExecutorInvoked</c> olaylari hicbir dugumle eslesmezdi.
/// </para>
/// <para>
/// Tip Microsoft Agent Framework tipi tasimaz: HTTP katmani
/// <c>AgentPrism.Workflows</c> paketine bagli degildir (K-118).
/// </para>
/// </remarks>
public sealed record WorkflowGraph
{
    /// <summary>Grafin ait oldugu workflow'un adi.</summary>
    public required string Name { get; init; }

    /// <summary>Girdi mesajini ilk alan dugumun kimligi.</summary>
    public required string StartExecutorId { get; init; }

    /// <summary>Graftaki dugumler.</summary>
    public IReadOnlyList<WorkflowGraphNode> Nodes { get; init; } = [];

    /// <summary>Dugumler arasindaki kenarlar.</summary>
    public IReadOnlyList<WorkflowGraphEdge> Edges { get; init; } = [];

    /// <summary>
    /// Microsoft Agent Framework'un urettigi Mermaid metni.
    /// </summary>
    /// <remarks>
    /// Arayuz grafi kendi cizer (bundle butcesi, K-002); bu metin
    /// <em>disari aktarma</em> icindir. Kullanici panoya kopyalayip bir
    /// dokumana yapistirabilir - proje kurali diyagramlari Mermaid ile ister.
    /// </remarks>
    public required string Mermaid { get; init; }
}

/// <summary>Graftaki bir dugum.</summary>
/// <remarks>
/// Kimlik, calistirma sirasinda gelen <c>ExecutorInvoked</c> /
/// <c>ExecutorCompleted</c> / <c>ExecutorFailed</c> olaylarinin <c>Text</c>
/// alaniyla <strong>birebir</strong> eslesir; arayuz dugumleri bu sayede canli
/// renklendirir.
/// </remarks>
public sealed record WorkflowGraphNode
{
    /// <summary>Executor kimligi.</summary>
    public required string Id { get; init; }

    /// <summary>Arayuzde gosterilecek kisa etiket.</summary>
    public required string Label { get; init; }

    /// <summary>Dugumun rolu.</summary>
    public required WorkflowNodeKind Kind { get; init; }

    /// <summary>
    /// Dugum bir agent'i temsil ediyorsa agent'in adi; aksi hâlde
    /// <see langword="null"/>.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>Microsoft Agent Framework'un executor tipi. Hata ayiklama icin.</summary>
    public string? ExecutorType { get; init; }
}

/// <summary>Iki dugum arasindaki baglanti.</summary>
public sealed record WorkflowGraphEdge
{
    /// <summary>Kaynak dugumun kimligi.</summary>
    public required string From { get; init; }

    /// <summary>Hedef dugumun kimligi.</summary>
    public required string To { get; init; }

    /// <summary>Kenarin turu.</summary>
    public required WorkflowEdgeKind Kind { get; init; }
}

/// <summary>Bir graf dugumunun rolu.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir (K-040). Arayuz dugum bicimini buna gore secer.
/// </remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<WorkflowNodeKind>))]
public enum WorkflowNodeKind
{
    /// <summary>Rolu belirlenemeyen dugum.</summary>
    Unknown = 0,

    /// <summary>Katalogdaki bir agent'i calistiran dugum.</summary>
    Agent = 1,

    /// <summary>Hazir desenin ekledigi yardimci dugum (dagitici, birlestirici, yonetici).</summary>
    Orchestration = 2,

    /// <summary>Disaridan yanit bekleyen port. Human-in-the-loop buradan girer.</summary>
    RequestPort = 3,

    /// <summary>Grafin ciktisini toplayan dugum.</summary>
    Output = 4,
}

/// <summary>Bir kenarin turu.</summary>
/// <remarks>JSON'da ad olarak yazilir (K-040).</remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<WorkflowEdgeKind>))]
public enum WorkflowEdgeKind
{
    /// <summary>Tek kaynaktan tek hedefe.</summary>
    Direct = 0,

    /// <summary>Tek kaynaktan birden cok hedefe.</summary>
    FanOut = 1,

    /// <summary>Birden cok kaynaktan tek hedefe.</summary>
    FanIn = 2,
}
