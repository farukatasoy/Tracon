using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Tracon;

[Description("The severity of a ValidationMessage.")]
[JsonConverter(typeof(JsonStringEnumConverter<ValidationSeverity>))]
internal enum ValidationSeverity
{
    /// <summary>The definition cannot be built. <c>AgentValidationReport.Valid</c> is therefore <see langword="false"/>.</summary>
    Error = 1,

    /// <summary>The definition can be built, but a check could not finish or something needs attention.</summary>
    Warning = 2,
}
