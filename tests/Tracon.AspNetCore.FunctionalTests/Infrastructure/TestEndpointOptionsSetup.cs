namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Changes the endpoint options a test host maps with, from a place that only has the service collection.
/// </summary>
/// <remarks>
/// The endpoint options are built by <c>MapTracon</c> and are not in the container, so a helper that answers a decision
/// through <c>ITraconBuilder</c> (the production profile tests) cannot reach them. It registers one of these and
/// <see cref="TraconTestHost"/> applies every registered one when it maps the endpoints.
/// </remarks>
/// <param name="apply">What to change.</param>
internal sealed class TestEndpointOptionsSetup(Action<TraconEndpointOptions> apply)
{
    /// <summary>Applies the change.</summary>
    /// <param name="options">The options <c>MapTracon</c> is about to use.</param>
    public void Apply(TraconEndpointOptions options) => apply(options);
}
