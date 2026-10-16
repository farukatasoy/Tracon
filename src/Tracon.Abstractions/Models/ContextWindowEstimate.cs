using System.ComponentModel;

namespace Tracon;

[Description(
    "The result of a pre-flight context-window check, computed without calling\n" +
    "the model provider.")]
internal sealed record ContextWindowEstimate
{
    [Description("Gets the estimated token count of the prompt. The estimate is approximate.")]
    public required int PromptTokens { get; init; }

    [Description(
        "Gets the model's context window size, taken from the model catalog's\n" +
        "`ModelDescriptor.ContextWindowTokens`.\n" +
        "`null` when the model is not found in the catalog.")]
    public required int? ContextWindowTokens { get; init; }

    [Description(
        "Gets the token budget kept for the prompt after\n" +
        "`TraconPreflightOptions.ReserveRatio` is set aside for\n" +
        "the answer. `null` when `ContextWindowTokens` is unknown.")]
    public required int? AllowedPromptTokens { get; init; }

    [Description(
        "Gets whether a real run with this prompt would be rejected by the\n" +
        "pre-flight check. Always `false` when\n" +
        "`ContextWindowTokens` is unknown — an unknown window can never\n" +
        "be exceeded.")]
    public required bool WouldBeRejected { get; init; }
}
