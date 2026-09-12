namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// A fake content guard whose decision is set by the test.
/// </summary>
/// <remarks>
/// Independent of the built-in <c>PatternContentGuard</c>: it verifies the
/// pipeline's behavior (placement, severity order, recording) without mixing
/// in pattern-matching logic.
/// </remarks>
internal sealed class StubContentGuard(
    Func<ContentGuardContext, ContentGuardResult> decide,
    string name = "stub") : IContentGuard
{
    /// <summary>The number of times the guard was called.</summary>
    public int CallCount { get; private set; }

    /// <summary>The texts the guard saw, in call order.</summary>
    public List<string> SeenText { get; } = [];

    /// <summary>The directions the guard saw, in call order.</summary>
    public List<ContentGuardDirection> SeenDirections { get; } = [];

    /// <summary>The sources the guard saw, in call order.</summary>
    public List<ContentGuardSource> SeenSources { get; } = [];

    /// <summary>The tool names the guard saw, in call order.</summary>
    public List<string?> SeenToolNames { get; } = [];

    /// <summary>The last context the guard saw.</summary>
    public ContentGuardContext? LastContext { get; private set; }

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public ValueTask<ContentGuardResult> InspectAsync(
        ContentGuardContext context,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        SeenText.Add(context.Text);
        SeenDirections.Add(context.Direction);
        SeenSources.Add(context.Source);
        SeenToolNames.Add(context.ToolName);
        LastContext = context;

        return ValueTask.FromResult(decide(context));
    }

    /// <summary>Produces a guard that blocks when it sees the given text.</summary>
    public static StubContentGuard Blocking(string trigger, string name = "stub")
        => new(
            context => context.Text.Contains(trigger, StringComparison.OrdinalIgnoreCase)
                ? ContentGuardResult.Block("trigger", "Test rule matched.")
                : ContentGuardResult.Allow,
            name);

    /// <summary>Produces a guard that masks when it sees the given text.</summary>
    public static StubContentGuard Masking(string trigger, string replacement, string name = "stub")
        => new(
            context => context.Text.Contains(trigger, StringComparison.OrdinalIgnoreCase)
                ? ContentGuardResult.Mask(
                    context.Text.Replace(trigger, replacement, StringComparison.OrdinalIgnoreCase),
                    "trigger")
                : ContentGuardResult.Allow,
            name);
}
