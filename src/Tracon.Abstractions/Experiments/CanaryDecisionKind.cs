using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Defines the outcome of a canary evaluation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CanaryDecisionKind>))]
public enum CanaryDecisionKind
{
    /// <summary>
    /// The control or canary variant has not reached <see
    /// cref="CanaryPolicy.MinSampleSize"/>; no decision is made.
    /// </summary>
    InsufficientData = 0,

    /// <summary>The canary is not worse than the control by the threshold; traffic continues.</summary>
    Healthy = 1,

    /// <summary>The canary is worse than the control by the threshold and must roll back.</summary>
    RollBack = 2,
}
