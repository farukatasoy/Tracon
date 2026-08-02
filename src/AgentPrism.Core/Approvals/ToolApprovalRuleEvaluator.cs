using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Kalici onay kurallarini bir tool cagrisina uygular.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework'un <c>ToolApprovalAgentOptions.AutoApprovalRules</c>
/// yapisina baglanir. Kural eslesirse cagri kullaniciya sorulmadan calisir.
/// </para>
/// <para>
/// <strong>Kiraci sinirini bu sinif korur.</strong> Kurallar her zaman
/// <see cref="ITenantContext.TenantId"/> ile okunur; bir kiracinin verdigi
/// onay baska bir kiracinin cagrisini calistiramaz.
/// </para>
/// <para>
/// <strong>Depo hatasi onay vermez.</strong> Kural okunamazsa cagri
/// otomatik onay almaz ve kullaniciya sorulur. Guvenli taraf budur.
/// </para>
/// </remarks>
public sealed class ToolApprovalRuleEvaluator
{
    private readonly IToolApprovalRuleStore _rules;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ToolApprovalRuleEvaluator> _logger;

    /// <summary>Yeni bir degerlendirici olusturur.</summary>
    /// <param name="rules">Kural deposu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public ToolApprovalRuleEvaluator(
        IToolApprovalRuleStore rules,
        ITenantContext tenantContext,
        ILogger<ToolApprovalRuleEvaluator> logger)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(logger);

        _rules = rules;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Bir tool cagrisinin kalici bir kuralla otomatik onaylanip
    /// onaylanmadigini soyler.
    /// </summary>
    /// <param name="agentName">Cagriyi yapan agent.</param>
    /// <param name="call">Tool cagrisi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Cagri otomatik onayliysa <see langword="true"/>.</returns>
    public async ValueTask<bool> IsAutoApprovedAsync(
        string agentName,
        FunctionCallContent call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        var tenantId = _tenantContext.TenantId;

        IReadOnlyList<ToolApprovalRule> rules;

        try
        {
            rules = await _rules.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Onay kurallari okunamadi; '{ToolName}' cagrisi icin onay sorulacak.",
                call.Name);

            return false;
        }

        if (rules.Count == 0)
        {
            return false;
        }

        string? argumentsHash = null;

        foreach (var rule in rules)
        {
            if (!string.Equals(rule.ToolName, call.Name, StringComparison.Ordinal))
            {
                continue;
            }

            // AgentName bos ise kural kiracinin tum agent'larini kapsar.
            if (rule.AgentName is { } scoped && !string.Equals(scoped, agentName, StringComparison.Ordinal))
            {
                continue;
            }

            if (rule.ArgumentsHash is null)
            {
                return true;
            }

            argumentsHash ??= ComputeArgumentsHash(call.Arguments);

            if (string.Equals(rule.ArgumentsHash, argumentsHash, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tool argumanlarindan kararli bir parmak izi uretir.
    /// </summary>
    /// <param name="arguments">Cagri argumanlari.</param>
    /// <returns>Onaltilik parmak izi. Arguman yoksa bos dize.</returns>
    /// <remarks>
    /// <para>
    /// Anahtarlar siralanir: sozluk sirasi calistirmalar arasinda degisebilir ve
    /// ayni cagri farkli parmak izi uretirse "bir daha sorma" kurali hicbir zaman
    /// eslesmezdi.
    /// </para>
    /// <para>
    /// JSON serilestirme <em>kullanilmaz</em>: yansimaya dayanan serilestirme
    /// <c>IL2026</c> uretir ve <c>AgentPrism.Core</c> AOT uyumlu isaretlidir.
    /// </para>
    /// </remarks>
    public static string ComputeArgumentsHash(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var pair in arguments.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key)
                .Append('=')
                .Append(Convert.ToString(pair.Value, CultureInfo.InvariantCulture))
                // Ayrac birim ayirici (U+001F): metin degerlerde gecmez, boylece
                // farkli sozlukler ayni parmak izini uretemez.
                .Append('\u001F');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexString(hash);
    }
}
