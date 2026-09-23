namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>
/// The one test collection every console test class belongs to.
/// </summary>
/// <remarks>
/// Phase 184 split <c>UiTests.cs</c> into one class per screen. xunit runs
/// collections in parallel and the tests inside one collection one at a time;
/// before the split every console test was in ONE class, and therefore ran one
/// at a time. One collection keeps exactly that, so the split changes where the
/// tests live and nothing about how they run. Whether they may run in parallel
/// is a separate decision, made by measurement (Phase 184.4).
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ConsoleScreens
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Console";
}
