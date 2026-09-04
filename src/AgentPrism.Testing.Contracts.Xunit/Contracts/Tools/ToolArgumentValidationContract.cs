using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Tools;

/// <summary>
/// Behavior tests for the consumer's own <see cref="IToolArgumentsValidator"/>,
/// fuzzed against one of the consumer's own tools.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IToolArgumentsValidator"/> promises fail-closed behavior: a
/// missing required field, a type mismatch, an out-of-range number, or a
/// pattern violation must be rejected, and a validator that throws must still
/// end up rejecting the call. AgentPrism tests that promise's own wrapper
/// (<c>ValidatingAIFunction</c>) against synthetic validators; this contract
/// tests whether the <strong>consumer's</strong> validator actually implements
/// the rule, using arguments generated from <see cref="Tool"/>'s own
/// <see cref="AIFunctionDeclaration.JsonSchema"/>.
/// </para>
/// <para>
/// This contract does not ship a validator and does not require one to
/// reject or accept an unrecognized extra property either way — AgentPrism's
/// stance is that JSON Schema validation, including <c>additionalProperties</c>,
/// stays inside the consumer's own trust boundary. Override
/// <see cref="ExtraPropertyIsRejected"/> to state which way <see cref="Validator"/>
/// goes; the one thing this contract will not accept is silence.
/// </para>
/// <para>
/// A scenario this generator cannot build for <see cref="Tool"/>'s schema (no
/// required property, no numeric bound, no pattern, or a required property
/// shaped like a nested object) is skipped with an explicit reason rather
/// than passing silently.
/// </para>
/// </remarks>
public abstract class ToolArgumentValidationContract : IAsyncLifetime
{
    /// <summary>The tool whose schema drives the generated arguments.</summary>
    protected AIFunction Tool { get; private set; } = null!;

    /// <summary>The consumer's own validator under test.</summary>
    protected IToolArgumentsValidator Validator { get; private set; } = null!;

    /// <summary>Creates the tool under test.</summary>
    protected abstract ValueTask<AIFunction> CreateToolAsync();

    /// <summary>Creates the validator under test.</summary>
    protected abstract ValueTask<IToolArgumentsValidator> CreateValidatorAsync();

    /// <summary>The seed the argument generator uses. Fixed by default for reproducibility (143.2).</summary>
    protected virtual int Seed => 20260903;

    /// <summary>
    /// Whether <see cref="Validator"/> is expected to reject an argument set
    /// that carries a property <see cref="Tool"/>'s schema never declared.
    /// </summary>
    protected virtual bool ExtraPropertyIsRejected => true;

    private ToolDescriptor Descriptor => new() { Name = Tool.Name, Description = Tool.Description, JsonSchema = Tool.JsonSchema.GetRawText() };

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        Tool = await CreateToolAsync().ConfigureAwait(false);
        Validator = await CreateValidatorAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    [Fact]
    public async Task Missing_required_argument_is_rejected()
    {
        var mutation = new SchemaArgumentGenerator(Seed).MissingRequiredProperty(Tool.JsonSchema);
        Assert.SkipWhen(mutation.IsSkipped, mutation.SkipReason ?? "unsupported schema shape.");

        await AssertRejectedAsync(mutation.Arguments!, "an argument set missing a required property");
    }

    [Fact]
    public async Task Type_mismatch_is_rejected()
    {
        var mutation = new SchemaArgumentGenerator(Seed).TypeMismatch(Tool.JsonSchema);
        Assert.SkipWhen(mutation.IsSkipped, mutation.SkipReason ?? "unsupported schema shape.");

        await AssertRejectedAsync(mutation.Arguments!, "an argument set with a property of the wrong JSON type");
    }

    [Fact]
    public async Task Out_of_range_number_is_rejected()
    {
        var mutation = new SchemaArgumentGenerator(Seed).OutOfRangeNumber(Tool.JsonSchema);
        Assert.SkipWhen(mutation.IsSkipped, mutation.SkipReason ?? "unsupported schema shape.");

        await AssertRejectedAsync(mutation.Arguments!, "an argument set with a number outside its declared bound");
    }

    [Fact]
    public async Task Pattern_violation_is_rejected()
    {
        var mutation = new SchemaArgumentGenerator(Seed).PatternViolation(Tool.JsonSchema);
        Assert.SkipWhen(mutation.IsSkipped, mutation.SkipReason ?? "unsupported schema shape.");

        await AssertRejectedAsync(mutation.Arguments!, "an argument set violating a declared pattern");
    }

    [Fact]
    public async Task Unknown_extra_property_is_handled_deliberately()
    {
        var mutation = new SchemaArgumentGenerator(Seed).ExtraProperty(Tool.JsonSchema);
        Assert.SkipWhen(mutation.IsSkipped, mutation.SkipReason ?? "unsupported schema shape.");

        var result = await Validator.ValidateAsync(Descriptor, mutation.Arguments!, CancellationToken.None).ConfigureAwait(false);

        result.IsValid.ShouldBe(
            !ExtraPropertyIsRejected,
            $"'{GetType().Name}' declares ExtraPropertyIsRejected = {ExtraPropertyIsRejected}, but the validator did the opposite.");
    }

    [Fact]
    public async Task Throwing_validator_rejects_the_call()
    {
        var mutation = new SchemaArgumentGenerator(Seed).Poison(Tool.JsonSchema);
        await AssertRejectedAsync(mutation.Arguments!, "a poisoned argument set most likely to fault a naive validator's own internal code");
    }

    [Fact]
    public async Task A_pre_cancelled_token_is_honored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async ()
            => await Validator.ValidateAsync(Descriptor, new AIFunctionArguments(StringComparer.Ordinal), cancellation.Token).ConfigureAwait(false));
    }

    private async Task AssertRejectedAsync(AIFunctionArguments arguments, string scenario)
    {
        ToolArgumentsValidationResult result;

        try
        {
            result = await Validator.ValidateAsync(Descriptor, arguments, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // AgentPrism's own wrapper (ValidatingAIFunction) turns a thrown
            // exception into a rejection — fail-closed holds either way.
            return;
        }

        result.IsValid.ShouldBeFalse($"The validator accepted {scenario}, which violates the tool's own JSON Schema.");
    }
}
