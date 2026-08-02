using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismToolRegistration"/> kayitlarindan olusturulan tool defteri.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, AIFunction> _tools;
    private readonly List<ToolDescriptor> _descriptors;

    /// <summary>Kayitlardan yeni bir defter olusturur.</summary>
    /// <param name="registrations">Tool kayitlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Ayni ad birden cok kez kaydedilmisse.</exception>
    public ToolRegistry(IEnumerable<AgentPrismToolRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _tools = new Dictionary<string, AIFunction>(StringComparer.Ordinal);
        _descriptors = [];

        foreach (var registration in registrations)
        {
            var name = registration.Function.Name;

            // Onay sarmalamasi BURADA yapilir, derleyicide degil. Defter,
            // "bir agent yalnizca kayitli bir tool'a isaret edebilir" kuralinin
            // zorlandigi tek yerdir; onay zorunlulugunu da ayni yerde zorlamak
            // baska bir kod yolunun sarmalamayi atlamasini imkansiz kilar.
            //
            // ApprovalRequiredAIFunction bir DelegatingAIFunction'dir: ad,
            // aciklama ve JSON semasi degismez. Microsoft Agent Framework
            // sarmalanmis bir tool'u calistirmak yerine
            // ToolApprovalRequestContent uretir.
            var function = registration.RequiresApproval
                ? new ApprovalRequiredAIFunction(registration.Function)
                : registration.Function;

            if (!_tools.TryAdd(name, function))
            {
                throw new AgentPrismException(
                    $"'{name}' adinda birden cok tool kaydedilmis. Tool adlari benzersiz olmalidir.");
            }

            _descriptors.Add(new ToolDescriptor
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

        _descriptors.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDescriptor> List() => _descriptors;

    /// <inheritdoc />
    public bool TryGet(string name, [NotNullWhen(true)] out AIFunction? tool)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _tools.TryGetValue(name, out tool);
    }
}
