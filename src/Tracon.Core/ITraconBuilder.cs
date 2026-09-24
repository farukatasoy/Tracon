using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// The fluent chain that configures Tracon. Returned from the
/// <c>AddTracon()</c> call.
/// </summary>
/// <remarks>
/// <para>
/// The chain carries only <see cref="Services"/>. Every registration
/// capability is an extension method: Tracon's own live in
/// <see cref="TraconBuilderExtensions"/>, and provider and storage packages
/// add theirs (<c>UsePostgreSql()</c>, <c>UseOpenAI()</c>, and so on).
/// </para>
/// <para>
/// <c>AddTracon()</c> returns Tracon's implementation. A chain of your own,
/// for example a test double, only has to supply <see cref="Services"/>; the
/// extension methods run the same way against it.
/// </para>
/// </remarks>
public interface ITraconBuilder
{
    /// <summary>The underlying service collection.</summary>
    /// <remarks>
    /// <para>
    /// The escape hatch: anything Tracon does not model is registered here.
    /// Every Tracon service is registered with <c>TryAdd</c>, so registration
    /// ORDER decides who wins, and the two seam shapes behave differently.
    /// </para>
    /// <para>
    /// For a single-instance seam (for example <see cref="ITenantContext"/>): a
    /// registration made before <c>AddTracon()</c> wins outright, and only
    /// one registration remains. A registration made after <c>AddTracon()</c>
    /// also wins for a direct resolve, but Tracon's own registration is not
    /// removed - it stays behind as a second, unused entry.
    /// </para>
    /// <para>
    /// For a multi-registration seam (for example <see cref="IAgentDecorator"/>):
    /// a registration made before <c>AddTracon()</c> joins the list alongside
    /// the built-in ones. A registration made after <c>AddTracon()</c> also
    /// joins the list - the built-in implementation keeps running too, which is a
    /// real behavior difference from the single-instance case above.
    /// </para>
    /// <example>
    /// <code>
    /// // Runs before AddTracon(), so this registration wins outright.
    /// builder.Services.AddSingleton(new OrderGateway());
    /// builder.AddTracon();
    /// </code>
    /// </example>
    /// </remarks>
    IServiceCollection Services { get; }
}
