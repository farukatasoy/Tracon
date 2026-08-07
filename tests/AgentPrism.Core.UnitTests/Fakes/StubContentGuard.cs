namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// Kararini testin belirledigi sahte icerik guard'i.
/// </summary>
/// <remarks>
/// Yerlesik <c>PatternContentGuard</c>'dan bagimsizdir: boru hattinin davranisini
/// (konum, siddet sirasi, kayit) desen mantigina bulastirmadan dogrular.
/// </remarks>
internal sealed class StubContentGuard(
    Func<ContentGuardContext, ContentGuardResult> decide,
    string name = "stub") : IContentGuard
{
    /// <summary>Guard kac kez cagrildi.</summary>
    public int CallCount { get; private set; }

    /// <summary>Guard'in gordugu metinler, cagri sirasiyla.</summary>
    public List<string> SeenText { get; } = [];

    /// <summary>Guard'in gordugu yonler, cagri sirasiyla.</summary>
    public List<ContentGuardDirection> SeenDirections { get; } = [];

    /// <summary>Guard'in gordugu son baglam.</summary>
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
        LastContext = context;

        return ValueTask.FromResult(decide(context));
    }

    /// <summary>Verilen metni gorurse engelleyen bir guard uretir.</summary>
    public static StubContentGuard Blocking(string trigger, string name = "stub")
        => new(
            context => context.Text.Contains(trigger, StringComparison.OrdinalIgnoreCase)
                ? ContentGuardResult.Block("trigger", "Test kurali eslesti.")
                : ContentGuardResult.Allow,
            name);

    /// <summary>Verilen metni gorurse maskeleyen bir guard uretir.</summary>
    public static StubContentGuard Masking(string trigger, string replacement, string name = "stub")
        => new(
            context => context.Text.Contains(trigger, StringComparison.OrdinalIgnoreCase)
                ? ContentGuardResult.Mask(
                    context.Text.Replace(trigger, replacement, StringComparison.OrdinalIgnoreCase),
                    "trigger")
                : ContentGuardResult.Allow,
            name);
}
