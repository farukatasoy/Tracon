using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Reports where a run's cost pricing came from.</summary>
/// <remarks>
/// Written <strong>as a name</strong> in JSON; stored as <c>smallint</c> in
/// the database. The value order must not change (see <c>runs.pricing_source</c>).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PricingSource>))]
public enum PricingSource
{
    /// <summary>The price came from the model catalog (<see cref="ModelDescriptor"/>).</summary>
    Catalog = 0,

    /// <summary>The price came from the <c>Tracon:Pricing</c> configuration.</summary>
    Configuration = 1,

    /// <summary>
    /// The price is not defined in any source. The cost fields are
    /// <see langword="null"/> in this case — <strong>not zero</strong>: zero
    /// would mean the model is free.
    /// </summary>
    Unknown = 2,
}
