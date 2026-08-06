using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir <see cref="ValidationMessage"/>'in onem derecesi.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValidationSeverity>))]
public enum ValidationSeverity
{
    /// <summary>Tanim derlenemez. <see cref="AgentValidationReport.Valid"/> bu yuzden <see langword="false"/> olur.</summary>
    Error = 1,

    /// <summary>Tanim derlenebilir ama denetim tam sonuclanamadi veya dikkat gerektiren bir durum var.</summary>
    Warning = 2,
}
