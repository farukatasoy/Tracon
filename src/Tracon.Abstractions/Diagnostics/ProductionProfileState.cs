namespace Tracon;

/// <summary>What a production profile check found.</summary>
/// <remarks>
/// Three values, not two. A decision that is <em>meaningless</em> in a
/// composition is not the same as one that was skipped, and collapsing the two
/// forces a consumer to accept an item that does not apply to them — which
/// fills the accept list with noise and hides the accepts that matter.
/// </remarks>
public enum ProductionProfileState
{
    /// <summary>The decision was answered: the feature is on.</summary>
    Satisfied,

    /// <summary>
    /// The decision was skipped: the setting is still on its permissive
    /// default. The host does not start unless the risk was accepted by name.
    /// </summary>
    Permissive,

    /// <summary>
    /// The decision does not apply to this composition. The host starts, and
    /// the item is still reported so it is visible rather than silently
    /// missing.
    /// </summary>
    NotApplicable,
}
