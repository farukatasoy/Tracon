using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir kanarya degerlendirmesinin sonucu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CanaryDecisionKind>))]
public enum CanaryDecisionKind
{
    /// <summary>Kontrol veya kanarya kolu <see cref="CanaryPolicy.MinSampleSize"/>'a henuz ulasmadi; karar verilmedi.</summary>
    InsufficientData = 0,

    /// <summary>Kanarya kontrolden esik kadar kotu degil; trafik devam eder.</summary>
    Healthy = 1,

    /// <summary>Kanarya kontrolden esik kadar kotu; geri alinmalidir.</summary>
    RollBack = 2,
}
