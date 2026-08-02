using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Tek bir kiracinin kesfedilmis MCP tool'larinin degismez anlik goruntusu.
/// </summary>
internal sealed class McpTenantTools
{
    private readonly Dictionary<string, AIFunction> _tools;

    private McpTenantTools(Dictionary<string, AIFunction> tools, IReadOnlyList<ToolDescriptor> descriptors)
    {
        _tools = tools;
        Descriptors = descriptors;
    }

    /// <summary>Bos kume.</summary>
    public static McpTenantTools Empty { get; } = new([], []);

    /// <summary>Arayuze gosterilen tool tanimlari; ada gore sirali.</summary>
    public IReadOnlyList<ToolDescriptor> Descriptors { get; }

    /// <summary>Adi verilen tool'u getirir.</summary>
    /// <param name="name">Tool adi.</param>
    /// <param name="tool">Bulunan tool.</param>
    /// <returns>Tool kayitliysa <see langword="true"/>.</returns>
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunction? tool)
        => _tools.TryGetValue(name, out tool);

    /// <summary>Kayitlardan degismez bir kume kurar.</summary>
    /// <param name="registrations">Kesfedilmis tool kayitlari.</param>
    /// <param name="logger">Ad cakismalarinin bildirilecegi gunlukleyici.</param>
    /// <returns>Kume.</returns>
    /// <remarks>
    /// Ad cakismasi <strong>hata degildir</strong>. Kod defterinde cakisma
    /// derleme aninda fark edilir ve atmak dogrudur; MCP'de ad uzak sunucudan
    /// gelir ve iki sunucunun ayni adi uretmesi AgentPrism'i cokertmemelidir.
    /// Ikinci kayit atlanir ve uyari loglanir.
    /// </remarks>
    public static McpTenantTools Create(
        IReadOnlyList<AgentPrismToolRegistration> registrations,
        ILogger logger)
    {
        var tools = new Dictionary<string, AIFunction>(registrations.Count, StringComparer.Ordinal);
        var descriptors = new List<ToolDescriptor>(registrations.Count);

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // Onay sarmalamasi burada yapilir. Kodda kayitli tool'larda ayni
            // isi ToolRegistry yapar; MCP tool'lari o deftere girmedigi icin
            // sarmalama bu yolda tekrarlanir.
            var function = registration.RequiresApproval
                ? new ApprovalRequiredAIFunction(registration.Function)
                : registration.Function;

            if (!tools.TryAdd(name, function))
            {
                logger.LogWarning(
                    "MCP tool adi '{ToolName}' birden cok kez uretildi; ikinci kayit atlandi. " +
                    "Sunucu adlarini birbirinden ayirt edilebilir secin.",
                    name);

                continue;
            }

            descriptors.Add(new ToolDescriptor
            {
                Name = name,
                Description = registration.Function.Description,
                JsonSchema = registration.Function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Undefined
                    ? null
                    : registration.Function.JsonSchema.GetRawText(),
                RequiresApproval = registration.RequiresApproval,
                Source = registration.Source,
            });
        }

        descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return new McpTenantTools(tools, descriptors);
    }
}
