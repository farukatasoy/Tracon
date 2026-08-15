using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// Ses tool'larinin ortak tabani: bagimlilik cozumu ve sema tasima.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Tool'lar <c>AIFunctionFactory</c> ile DEGIL, elle turetilerek yazilir.
/// Fabrika yansima kullanir ve <c>[RequiresUnreferencedCode]</c> +
/// <c>[RequiresDynamicCode]</c> tasir; paket AOT uyumlu isaretli oldugu icin bu
/// yol kapalidir. JSON semasi da bu yuzden elle yazilir — uc tool'un toplam uc
/// parametresi vardir, maliyeti dusuktur.
/// </para>
/// <para>
/// 🚨 <strong>Bagimliliklar KURULUM aninda alinir, cagri aninda degil.</strong>
/// <c>AIFunctionArguments.Services</c> AgentPrism'in boru hattinda
/// <strong>kullanilamaz</strong>: olculdu (2026-08-05, ornek uygulama) —
/// Microsoft Agent Framework tool'a <c>Microsoft.Extensions.AI.EmptyServiceProvider</c>
/// gecirir ve hicbir servis cozulmez. Hata yalnizca GERCEK bir tool cagrisinda
/// gorunur; birim testi sahte bir saglayici gecirdigi icin yakalamaz.
/// Ayrinti: <c>docs/28-SES-TOOLLARI.md</c>, bolum 28.0/G4.
/// </para>
/// </remarks>
internal abstract class VoiceToolBase : AIFunction
{
    private readonly IServiceProvider _services;

    /// <summary>Semayi ve servis saglayiciyi alir.</summary>
    /// <param name="services">Kurulum anindaki servis saglayici.</param>
    /// <param name="schema">Argumanlarin JSON semasi.</param>
    protected VoiceToolBase(IServiceProvider services, string schema)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        JsonSchema = JsonSerializer.Deserialize(schema, VoiceToolJsonContext.Default.JsonElement);
    }

    /// <inheritdoc />
    public override JsonElement JsonSchema { get; }

    /// <summary>Bir servisi cozer.</summary>
    /// <typeparam name="T">Servis tipi.</typeparam>
    /// <returns>Cozulen servis.</returns>
    /// <remarks>
    /// Kok saglayicidan cozulur. Tool'un ihtiyac duydugu servislerin tamami
    /// (<c>IAttachmentStore</c>, <c>ITenantContext</c>, <c>AttachmentTypeGuard</c>)
    /// singleton'dir; kiraci bilgisi <c>ITenantContext</c> icindeki
    /// <c>IHttpContextAccessor</c> uzerinden gelir, kapsamdan degil.
    /// </remarks>
    protected T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>Zorunlu bir metin argumanini okur.</summary>
    /// <param name="arguments">Cagri argumanlari.</param>
    /// <param name="name">Arguman adi.</param>
    /// <returns>Deger.</returns>
    /// <exception cref="AgentPrismException">Arguman yoksa veya bos ise.</exception>
    protected static string RequireText(AIFunctionArguments arguments, string name)
        => OptionalText(arguments, name)
           ?? throw new AgentPrismException($"'{name}' argumani zorunludur ve cannot be empty.");

    /// <summary>Istege bagli bir metin argumanini okur.</summary>
    /// <param name="arguments">Cagri argumanlari.</param>
    /// <param name="name">Arguman adi.</param>
    /// <returns>Deger; yoksa <see langword="null"/>.</returns>
    /// <remarks>
    /// Deger bir <see cref="JsonElement"/> olarak gelebilir: model argumanlari
    /// JSON'dan gelir ve baglayici tur bilgisi olmadan cozer.
    /// </remarks>
    protected static string? OptionalText(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        var text = raw switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            _ => raw.ToString(),
        };

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

/// <summary>
/// Tool semalarini AOT uyumlu cozmek icin kaynak uretilmis baglam.
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonElement))]
internal sealed partial class VoiceToolJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
