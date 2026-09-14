namespace Tracon;

/// <summary>
/// The risks a deployment has deliberately decided to carry, for
/// <c>RequireProductionProfile()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Acceptance is per item and by name. There is no "accept everything" switch
/// and none will be added: a blanket accept turns the gate back into the
/// silence it exists to remove, while one <c>Accept</c> line per decision shows
/// up in a code review and in the log.
/// </para>
/// <para>
/// Accepting a risk does NOT change any setting. It records that a human
/// looked at the decision and chose the permissive side.
/// </para>
/// </remarks>
public sealed class TraconProductionProfileOptions
{
    private readonly HashSet<TraconProductionRisk> _accepted = [];

    /// <summary>Gets the risks the deployment accepted, in no particular order.</summary>
    public IReadOnlyCollection<TraconProductionRisk> AcceptedRisks => _accepted;

    /// <summary>Accepts one risk, so it no longer stops the host.</summary>
    /// <param name="risk">The decision being accepted.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="risk"/> is not a defined value.</exception>
    /// <remarks>
    /// Accepting the same risk twice is a no-op, so a composition module that
    /// declares an accept cannot collide with another that declares the same
    /// one. The acceptance is written to the log at information level, by name,
    /// every time the host starts.
    /// </remarks>
    public TraconProductionProfileOptions Accept(TraconProductionRisk risk)
    {
        if (!Enum.IsDefined(risk))
        {
            throw new ArgumentOutOfRangeException(
                nameof(risk),
                risk,
                "Not a Tracon production risk.");
        }

        _accepted.Add(risk);

        return this;
    }
}
