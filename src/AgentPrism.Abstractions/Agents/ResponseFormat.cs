using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Agent yanitinin istenen bicimi.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentResponseFormatKind>))]
public enum AgentResponseFormatKind
{
    /// <summary>Duz metin. Acikca istenir.</summary>
    Text = 0,

    /// <summary>Gecerli bir JSON belgesi; sema dayatilmaz.</summary>
    Json = 1,

    /// <summary>Verilen JSON semasina uyan bir belge.</summary>
    JsonSchema = 2,
}

/// <summary>Yapilandirilmis cikti tanimi.</summary>
/// <remarks>
/// <para>
/// <see cref="ModelBinding.ResponseFormat"/> <see langword="null"/> ise bugunku
/// davranis degismez ve saglayiciya hicbir bicim kisiti gonderilmez. Bu tip
/// yalnizca kullanici acikca bir bicim istediginde devreye girer.
/// </para>
/// <para>
/// Doğrulama derleme aninda yapilir (<c>AgentDefinitionCompiler</c>): <see cref="Kind"/>
/// <see cref="AgentResponseFormatKind.JsonSchema"/> iken <see cref="Schema"/> bos
/// olamaz; diger kiplerde <see cref="Schema"/> dolu olamaz. <see cref="Schema"/>
/// bir JSON <strong>nesnesi</strong> olmalidir, icerigi doğrulanmaz.
/// </para>
/// </remarks>
public sealed record AgentResponseFormat
{
    /// <summary>Istenen bicim.</summary>
    public required AgentResponseFormatKind Kind { get; init; }

    /// <summary>
    /// JSON semasi. Yalnizca <see cref="AgentResponseFormatKind.JsonSchema"/>
    /// icin doldurulur ve bir JSON <strong>nesnesi</strong> olmalidir.
    /// </summary>
    public JsonElement? Schema { get; init; }

    /// <summary>Semanin adi. Saglayici bunu modele iletebilir.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Semanin aciklamasi.</summary>
    public string? SchemaDescription { get; init; }
}
