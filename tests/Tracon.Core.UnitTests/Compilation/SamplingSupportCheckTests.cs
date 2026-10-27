using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// F-606 and F-607 — a model can reject a request field that its provider's API surface sends, and a
/// provider can refuse (or silently change) a value outside the range its surface accepts. Both are
/// findings of the one check (<see cref="SamplingSupportCheck"/>) that the compiler and the validator read.
/// </summary>
/// <remarks>
/// <para>
/// The rules mirror <see cref="SamplingSupportTests"/>: one check for two readers, the primary model and every
/// fallback link, and a field is never dropped silently. A model absent from the catalog, and a catalog entry
/// that names nothing, are not checked (the catalog is metadata, not a validation list).
/// </para>
/// <para>
/// The helpers are those of <see cref="SamplingSupportTests"/>; the rest of this class builds the providers.
/// </para>
/// </remarks>
public sealed class SamplingSupportCheckTests
{
    private const string RejectingModel = "no-stop-model";

    private static readonly SamplingLimits IntegerSeeds = new() { MinSeed = int.MinValue, MaxSeed = int.MaxValue };

    private static readonly SamplingFields[] AllFields = Enum.GetValues<SamplingFields>().Where(static field => field != SamplingFields.None).ToArray();

    public static TheoryData<string> Fields() => SamplingSupportTests.Fields();

    // F-606 — the model's own rejection ------------------------------------------------------------

    /// <param name="field">The request field the definition sets and the catalog says the model rejects.</param>
    [Theory]
    [MemberData(nameof(Fields))]
    public void A_field_the_catalog_says_the_model_rejects_does_not_compile(string field)
    {
        var compiler = SamplingSupportTests.Compiler(Rejecting(Enum.Parse<SamplingFields>(field)));

        var exception = Should.Throw<TraconCompilationException>(
            () => compiler.Compile(SamplingSupportTests.Definition(SamplingSupportTests.Setting(field) with { Model = RejectingModel })));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain($"{nameof(ModelBinding)}.{field}", Case.Sensitive);
        exception.Message.ShouldContain($"'{RejectingModel}'");
        exception.Message.ShouldContain("'fake' provider");
        exception.Message.ShouldContain("rejects");
    }

    [Fact]
    public void A_field_the_model_does_not_reject_compiles_and_a_rejection_does_not_open_the_others()
    {
        var compiler = SamplingSupportTests.Compiler(Rejecting(SamplingFields.StopSequences));

        foreach (var field in AllFields.Where(static field => field != SamplingFields.StopSequences))
        {
            compiler.Compile(SamplingSupportTests.Definition(SamplingSupportTests.Setting(field.ToString()) with { Model = RejectingModel }))
                .ShouldNotBeNull(field.ToString());
        }

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(SamplingSupportTests.Definition(
            SamplingSupportTests.Setting(nameof(ModelBinding.StopSequences)) with { Model = RejectingModel, Seed = 7 })));

        exception.Message.ShouldContain(nameof(ModelBinding.StopSequences));
        exception.Message.ShouldNotContain(nameof(ModelBinding.Seed));
    }

    [Fact]
    public void A_catalog_that_names_no_rejected_field_changes_nothing()
    {
        // The regression: a default descriptor, and a binding that sets every request field at once.
        var everything = TestData.Binding(model: "fake-model") with
        {
            Seed = 7,
            TopK = 40,
            StopSequences = ["END"],
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.5f,
            ToolMode = "auto",
            AllowMultipleToolCalls = false,
            ReasoningOutput = "Summary",
        };

        new ModelDescriptor { Name = "x" }.RejectedSamplingFields.ShouldBe(SamplingFields.None);

        SamplingSupportTests.Compiler(new FakeModelProvider()).Compile(SamplingSupportTests.Definition(everything)).ShouldNotBeNull();
    }

    [Fact]
    public void A_model_the_catalog_does_not_list_is_not_checked()
    {
        var compiler = SamplingSupportTests.Compiler(Rejecting(SamplingFields.StopSequences));

        compiler.Compile(SamplingSupportTests.Definition(TestData.Binding(model: "another-model") with { StopSequences = ["END"] }))
            .ShouldNotBeNull();
    }

    [Fact]
    public void The_model_name_is_matched_without_regard_to_case()
    {
        var compiler = SamplingSupportTests.Compiler(Rejecting(SamplingFields.StopSequences));

        Should.Throw<TraconCompilationException>(() => compiler.Compile(SamplingSupportTests.Definition(
            TestData.Binding(model: RejectingModel.ToUpperInvariant()) with { StopSequences = ["END"] })));
    }

    [Fact]
    public void A_fallback_link_whose_model_rejects_the_field_does_not_compile()
    {
        // FallbackChatClient sends a link the same ChatOptions, with only the model name replaced.
        var primary = new FakeModelProvider();
        var backup = Rejecting(SamplingFields.StopSequences, name: "backup");
        var compiler = SamplingSupportTests.Compiler(primary, backup);

        var definition = SamplingSupportTests.Definition(TestData.Binding() with
        {
            StopSequences = ["END"],
            Fallbacks = [new ModelFallback { Provider = "backup", Model = RejectingModel }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("'backup' provider");
        exception.Message.ShouldContain($"'{RejectingModel}'");
        exception.Message.ShouldNotContain("'fake' provider");
    }

    [Fact]
    public void A_field_the_provider_does_not_send_is_reported_as_unsupported_and_not_as_rejected_as_well()
    {
        var provider = new FakeModelProvider(
            supportedSamplingFields: SamplingFields.None,
            models: [new ModelDescriptor { Name = RejectingModel, RejectedSamplingFields = SamplingFields.Seed }]);

        var exception = Should.Throw<TraconCompilationException>(() => SamplingSupportTests.Compiler(provider).Compile(
            SamplingSupportTests.Definition(TestData.Binding(model: RejectingModel) with { Seed = 7 })));

        exception.Message.ShouldContain("does not send it");
        exception.Message.ShouldNotContain("rejects");
    }

    [Fact]
    public async Task The_validation_report_names_each_rejected_field_with_its_path_the_model_and_the_provider()
    {
        var validator = SamplingSupportTests.Validator(Rejecting(SamplingFields.Seed | SamplingFields.StopSequences));

        var report = await validator.ValidateAsync(
            SamplingSupportTests.Definition(TestData.Binding(model: RejectingModel) with { Seed = 7, TopK = 40, StopSequences = ["END"] }),
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeFalse();
        report.Messages.Select(static message => (message.Code, message.Path, message.Severity)).ShouldBe(
        [
            ("sampling_field_rejected_by_model", "model.seed", ValidationSeverity.Error),
            ("sampling_field_rejected_by_model", "model.stopSequences", ValidationSeverity.Error),
        ]);
        report.Messages.ShouldAllBe(static message =>
            message.Message.Contains("'fake' provider", StringComparison.Ordinal)
            && message.Message.Contains($"'{RejectingModel}'", StringComparison.Ordinal));
        report.Messages.ShouldNotContain(static message => message.Code == "compilation_error");
    }

    [Fact]
    public async Task The_validation_report_checks_the_model_of_a_fallback_link()
    {
        var validator = SamplingSupportTests.Validator(
            new FakeModelProvider(),
            Rejecting(SamplingFields.StopSequences, name: "backup"));

        var report = await validator.ValidateAsync(
            SamplingSupportTests.Definition(TestData.Binding() with
            {
                StopSequences = ["END"],
                Fallbacks = [new ModelFallback { Provider = "backup", Model = RejectingModel }],
            }),
            TestContext.Current.CancellationToken);

        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("sampling_field_rejected_by_model");
        message.Path.ShouldBe("model.stopSequences");
        message.Message.ShouldContain("'backup' provider");
    }

    // F-607 — the provider's value ranges ----------------------------------------------------------

    /// <param name="seed">A seed outside the range of a 32-bit integer.</param>
    [Theory]
    [InlineData(3_000_000_000)]
    [InlineData((long)int.MaxValue + 1)]
    [InlineData((long)int.MinValue - 1)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void A_seed_outside_the_range_the_provider_accepts_does_not_compile(long seed)
    {
        var compiler = SamplingSupportTests.Compiler(new FakeModelProvider(samplingLimits: IntegerSeeds));

        var exception = Should.Throw<TraconCompilationException>(
            () => compiler.Compile(SamplingSupportTests.Definition(TestData.Binding() with { Seed = seed })));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain($"{nameof(ModelBinding)}.{nameof(ModelBinding.Seed)}", Case.Sensitive);
        exception.Message.ShouldContain(seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        exception.Message.ShouldContain("-2147483648");
        exception.Message.ShouldContain("2147483647");
        exception.Message.ShouldContain("'fake' provider");
    }

    /// <param name="seed">A seed inside the range of a 32-bit integer, the ends included.</param>
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(42)]
    public void A_seed_inside_the_range_compiles_and_the_ends_belong_to_it(long seed)
    {
        var compiler = SamplingSupportTests.Compiler(new FakeModelProvider(samplingLimits: IntegerSeeds));

        compiler.Compile(SamplingSupportTests.Definition(TestData.Binding() with { Seed = seed })).ShouldNotBeNull();
    }

    [Fact]
    public void One_end_of_the_range_can_be_stated_alone()
    {
        var upperOnly = SamplingSupportTests.Compiler(new FakeModelProvider(samplingLimits: new SamplingLimits { MaxSeed = 100 }));
        var lowerOnly = SamplingSupportTests.Compiler(new FakeModelProvider(samplingLimits: new SamplingLimits { MinSeed = 0 }));

        upperOnly.Compile(SamplingSupportTests.Definition(TestData.Binding() with { Seed = long.MinValue })).ShouldNotBeNull();
        lowerOnly.Compile(SamplingSupportTests.Definition(TestData.Binding() with { Seed = long.MaxValue })).ShouldNotBeNull();

        Should.Throw<TraconCompilationException>(() => upperOnly.Compile(SamplingSupportTests.Definition(TestData.Binding() with { Seed = 101 })))
            .Message.ShouldContain("up to 100");
        Should.Throw<TraconCompilationException>(() => lowerOnly.Compile(SamplingSupportTests.Definition(TestData.Binding() with { Seed = -1 })))
            .Message.ShouldContain("from 0");
    }

    [Fact]
    public void More_stop_sequences_than_the_provider_accepts_does_not_compile_and_the_limit_itself_does()
    {
        var compiler = SamplingSupportTests.Compiler(new FakeModelProvider(samplingLimits: new SamplingLimits { MaxStopSequences = 4 }));

        compiler.Compile(SamplingSupportTests.Definition(TestData.Binding() with { StopSequences = ["a", "b", "c", "d"] }))
            .ShouldNotBeNull();

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(SamplingSupportTests.Definition(
            TestData.Binding() with { StopSequences = ["a", "b", "c", "d", "e"] })));

        exception.Message.ShouldContain($"{nameof(ModelBinding)}.{nameof(ModelBinding.StopSequences)}", Case.Sensitive);
        exception.Message.ShouldContain("5 entries");
        exception.Message.ShouldContain("at most 4");
        exception.Message.ShouldContain("'fake' provider");
    }

    [Fact]
    public void An_empty_stop_list_sets_nothing_and_is_not_held_to_a_limit()
    {
        var compiler = SamplingSupportTests.Compiler(new FakeModelProvider(samplingLimits: new SamplingLimits { MaxStopSequences = 0 }));

        compiler.Compile(SamplingSupportTests.Definition(TestData.Binding() with { StopSequences = [] })).ShouldNotBeNull();
    }

    [Fact]
    public void A_provider_that_states_no_limit_checks_no_value()
    {
        var compiler = SamplingSupportTests.Compiler(new FakeModelProvider());

        compiler.Compile(SamplingSupportTests.Definition(TestData.Binding() with
        {
            Seed = long.MaxValue,
            StopSequences = [.. Enumerable.Range(0, 100).Select(static index => $"stop-{index}")],
        })).ShouldNotBeNull();
    }

    [Fact]
    public void A_limit_on_a_field_the_provider_does_not_send_is_not_reported_on_top_of_the_missing_field()
    {
        var provider = new FakeModelProvider(
            supportedSamplingFields: SamplingFields.None,
            samplingLimits: new SamplingLimits { MaxSeed = 1, MaxStopSequences = 1 });

        var exception = Should.Throw<TraconCompilationException>(() => SamplingSupportTests.Compiler(provider).Compile(
            SamplingSupportTests.Definition(TestData.Binding() with { Seed = 99, StopSequences = ["a", "b"] })));

        exception.Message.ShouldContain("does not send it");
        exception.Message.ShouldNotContain("accepts");
    }

    [Fact]
    public void A_fallback_link_is_held_to_the_range_of_its_own_provider()
    {
        // The link receives the primary call's Seed, so a link that would wrap it is wrong for the definition.
        var compiler = SamplingSupportTests.Compiler(
            new FakeModelProvider(),
            new FakeModelProvider(name: "backup", samplingLimits: IntegerSeeds));

        var definition = SamplingSupportTests.Definition(TestData.Binding() with
        {
            Seed = 3_000_000_000,
            Fallbacks = [new ModelFallback { Provider = "backup", Model = "fake-model" }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("'backup' provider");
        exception.Message.ShouldNotContain("'fake' provider");
    }

    [Fact]
    public async Task The_validation_report_names_the_value_the_range_and_the_path()
    {
        var validator = SamplingSupportTests.Validator(new FakeModelProvider(
            samplingLimits: new SamplingLimits { MinSeed = int.MinValue, MaxSeed = int.MaxValue, MaxStopSequences = 2 }));

        var report = await validator.ValidateAsync(
            SamplingSupportTests.Definition(TestData.Binding() with { Seed = 3_000_000_000, StopSequences = ["a", "b", "c"] }),
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeFalse();
        report.Messages.Select(static message => (message.Code, message.Path, message.Severity)).ShouldBe(
        [
            ("sampling_value_out_of_range", "model.seed", ValidationSeverity.Error),
            ("sampling_value_out_of_range", "model.stopSequences", ValidationSeverity.Error),
        ]);
        report.Messages[0].Message.ShouldContain("3000000000");
        report.Messages[1].Message.ShouldContain("3 entries");
        report.Messages.ShouldNotContain(static message => message.Code == "compilation_error");
    }

    [Fact]
    public async Task The_validation_report_checks_the_range_of_a_fallback_link()
    {
        var validator = SamplingSupportTests.Validator(
            new FakeModelProvider(),
            new FakeModelProvider(name: "backup", samplingLimits: IntegerSeeds));

        var report = await validator.ValidateAsync(
            SamplingSupportTests.Definition(TestData.Binding() with
            {
                Seed = 3_000_000_000,
                Fallbacks = [new ModelFallback { Provider = "backup", Model = "fake-model" }],
            }),
            TestContext.Current.CancellationToken);

        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("sampling_value_out_of_range");
        message.Path.ShouldBe("model.seed");
        message.Message.ShouldContain("'backup' provider");
    }

    [Fact]
    public async Task A_value_in_range_leaves_the_report_clean()
    {
        var validator = SamplingSupportTests.Validator(new FakeModelProvider(
            samplingLimits: new SamplingLimits { MinSeed = int.MinValue, MaxSeed = int.MaxValue, MaxStopSequences = 4 }));

        var report = await validator.ValidateAsync(
            SamplingSupportTests.Definition(TestData.Binding() with { Seed = int.MaxValue, StopSequences = ["a", "b", "c", "d"] }),
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldBeEmpty();
    }

    // Both rules at once -------------------------------------------------------------------------

    [Fact]
    public void A_field_the_model_rejects_is_not_reported_again_for_its_value()
    {
        // Removing the field is the one remedy; a second finding about the range of a field that must go anyway
        // would only send the author to the wrong fix.
        var provider = new FakeModelProvider(
            models: [new ModelDescriptor { Name = RejectingModel, RejectedSamplingFields = SamplingFields.StopSequences }],
            samplingLimits: new SamplingLimits { MaxStopSequences = 1 });

        var exception = Should.Throw<TraconCompilationException>(() => SamplingSupportTests.Compiler(provider).Compile(
            SamplingSupportTests.Definition(TestData.Binding(model: RejectingModel) with { StopSequences = ["a", "b"] })));

        exception.Message.ShouldContain("rejects");
        exception.Message.ShouldNotContain("accepts");
    }

    [Fact]
    public void One_provider_used_twice_reports_a_range_finding_once()
    {
        var provider = new FakeModelProvider(
            samplingLimits: IntegerSeeds,
            models: [new ModelDescriptor { Name = "fake-model" }, new ModelDescriptor { Name = "second-model" }]);

        var exception = Should.Throw<TraconCompilationException>(() => SamplingSupportTests.Compiler(provider).Compile(
            SamplingSupportTests.Definition(TestData.Binding() with
            {
                Seed = 3_000_000_000,
                Fallbacks = [new ModelFallback { Provider = "fake", Model = "second-model" }],
            })));

        exception.Message.Split("ModelBinding.Seed").Length.ShouldBe(2);
    }

    // The descriptor and the default member ----------------------------------------------------------

    [Fact]
    public void The_registry_hands_the_provider_limits_to_the_descriptor()
    {
        var limits = new SamplingLimits { MinSeed = -5, MaxSeed = 5, MaxStopSequences = 3 };
        var registry = TestData.Providers(new FakeModelProvider(samplingLimits: limits), new FieldsOnlyProvider(), new UndeclaringProvider());

        var descriptors = registry.List().ToDictionary(static descriptor => descriptor.Name, StringComparer.Ordinal);

        descriptors["fake"].SamplingLimits.ShouldBe(limits);
        descriptors[FieldsOnlyProvider.ProviderName].SamplingLimits.ShouldBeSameAs(SamplingLimits.None);
        descriptors[UndeclaringProvider.ProviderName].SamplingLimits.ShouldBeSameAs(SamplingLimits.None);
    }

    [Fact]
    public void A_provider_written_before_the_limits_member_existed_still_works_and_states_none()
    {
        // The class names no SamplingLimits at all, exactly as a provider compiled against the previous interface
        // does: the member has a default body, so it compiles, loads, and runs unchanged.
        ISamplingSupport provider = new FieldsOnlyProvider();

        provider.SamplingLimits.ShouldBeSameAs(SamplingLimits.None);
        provider.SamplingLimits.MinSeed.ShouldBeNull();
        provider.SamplingLimits.MaxSeed.ShouldBeNull();
        provider.SamplingLimits.MaxStopSequences.ShouldBeNull();

        SamplingSupportTests.Compiler(new FieldsOnlyProvider())
            .Compile(SamplingSupportTests.Definition(new ModelBinding
            {
                Provider = FieldsOnlyProvider.ProviderName,
                Model = "m",
                Seed = long.MaxValue,
                StopSequences = [.. Enumerable.Range(0, 20).Select(static index => $"stop-{index}")],
            }))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task A_provider_whose_limits_member_returns_null_states_no_limit_and_does_not_break_the_check()
    {
        // A provider is third-party code: a null answer must not become a NullReferenceException (a 500) in the
        // compiler or the validation endpoint. It is read as "no limit", as a provider that states nothing is.
        var provider = new NullLimitsProvider();
        var definition = SamplingSupportTests.Definition(new ModelBinding
        {
            Provider = NullLimitsProvider.ProviderName,
            Model = "m",
            Seed = long.MaxValue,
            StopSequences = [.. Enumerable.Range(0, 20).Select(static index => $"stop-{index}")],
        });

        TestData.Providers(provider).List().ShouldHaveSingleItem().SamplingLimits.ShouldBeSameAs(SamplingLimits.None);

        SamplingSupportTests.Compiler(provider).Compile(definition).ShouldNotBeNull();

        var report = await SamplingSupportTests.Validator(provider).ValidateAsync(definition, TestContext.Current.CancellationToken);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void The_limits_record_compares_by_value_and_none_states_nothing()
    {
        SamplingLimits.None.ShouldBe(new SamplingLimits());
        new SamplingLimits { MaxStopSequences = 4 }.ShouldBe(new SamplingLimits { MaxStopSequences = 4 });
        new SamplingLimits { MaxStopSequences = 4 }.ShouldNotBe(SamplingLimits.None);
    }

    /// <summary>A provider whose catalog holds a model that rejects <paramref name="rejected"/>.</summary>
    private static FakeModelProvider Rejecting(SamplingFields rejected, string name = "fake")
        => new(name: name, models: [new ModelDescriptor { Name = RejectingModel, RejectedSamplingFields = rejected }]);

    /// <summary>A provider written against <see cref="ISamplingSupport"/> as it was before the limits member.</summary>
    private sealed class FieldsOnlyProvider : IModelProvider, ISamplingSupport
    {
        public const string ProviderName = "fields-only";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "m" }];

        public SamplingFields SupportedSamplingFields => SamplingFields.Seed | SamplingFields.StopSequences;

        public IChatClient CreateChatClient(ModelBinding binding) => new FakeChatClient();
    }

    /// <summary>A provider that answers <see cref="ISamplingSupport.SamplingLimits"/> with <see langword="null"/>, which the contract forbids.</summary>
    private sealed class NullLimitsProvider : IModelProvider, ISamplingSupport
    {
        public const string ProviderName = "null-limits";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "m" }];

        public SamplingFields SupportedSamplingFields => SamplingFields.Seed | SamplingFields.StopSequences;

        public SamplingLimits SamplingLimits => null!;

        public IChatClient CreateChatClient(ModelBinding binding) => new FakeChatClient();
    }

    /// <summary>A provider written against <see cref="IModelProvider"/> alone.</summary>
    private sealed class UndeclaringProvider : IModelProvider
    {
        public const string ProviderName = "undeclaring";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "m" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new FakeChatClient();
    }
}
