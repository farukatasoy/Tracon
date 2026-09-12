namespace Tracon;

/// <summary>
/// Named ordinals for the <c>SelectRun</c>/<c>SelectRuns</c> reader, in
/// <c>SqlQueriesBase.RunColumnOrder</c>'s order.
/// </summary>
/// <remarks>
/// A cross-check test verifies that these values are exactly
/// <c>0..RunColumnOrder.Length - 1</c>, each named once. <c>Tree*</c> members are the
/// descendant-tree aggregate of the same logical value the unprefixed member reads
/// from the run's own row (for example <see cref="InputTokens"/> and
/// <see cref="TreeInputTokens"/>).
/// </remarks>
internal static class RunOrdinals
{
    public const int Id = 0;
    public const int TenantId = 1;
    public const int AgentName = 2;
    public const int SessionId = 3;
    public const int Status = 4;
    public const int StartedAt = 5;
    public const int CompletedAt = 6;
    public const int IsStreaming = 7;
    public const int InputTokens = 8;
    public const int OutputTokens = 9;
    public const int TotalTokens = 10;
    public const int EventCount = 11;
    public const int ErrorType = 12;
    public const int ErrorMessage = 13;
    public const int ModelId = 14;
    public const int ParentRunId = 15;
    public const int RootRunId = 16;
    public const int Depth = 17;
    public const int ChildCount = 18;
    public const int TreeInputTokens = 19;
    public const int TreeOutputTokens = 20;
    public const int TreeTotalTokens = 21;
    public const int UsageRows = 22;
    public const int Kind = 23;
    public const int WorkflowName = 24;
    public const int AgentVersion = 25;
    public const int ExperimentId = 26;
    public const int Variant = 27;
    public const int InputCost = 28;
    public const int OutputCost = 29;
    public const int CostCurrency = 30;
    public const int PricingSource = 31;
    public const int TreeCostInput = 32;
    public const int TreeCostOutput = 33;
    public const int TreeCostCurrency = 34;
    public const int UnknownPricingRows = 35;
    public const int PricingRows = 36;
    public const int ErrorClass = 37;
    public const int ErrorFingerprint = 38;
    public const int ReplayOfRunId = 39;
    public const int UserId = 40;
    public const int Labels = 41;
    public const int CachedInputTokens = 42;
    public const int ReasoningTokens = 43;
    public const int AudioInputTokens = 44;
    public const int AudioOutputTokens = 45;
    public const int CachedInputCost = 46;
    public const int TreeCachedInputTokens = 47;
    public const int TreeReasoningTokens = 48;
    public const int TreeAudioInputTokens = 49;
    public const int TreeAudioOutputTokens = 50;
    public const int TreeCostCachedInput = 51;
    public const int ContinuedFromRunId = 52;
    public const int ModelProvider = 53;
    public const int InputPricePerMillionTokens = 54;
    public const int OutputPricePerMillionTokens = 55;
    public const int CachedInputPricePerMillionTokens = 56;
}
