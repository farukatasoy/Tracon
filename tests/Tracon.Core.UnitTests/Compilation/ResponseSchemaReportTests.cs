using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The validation report carries the response schema each provider's client sends, and warns about the
/// two differences that change what the model is asked for (<see cref="IResponseSchemaSupport"/>).
/// </summary>
/// <remarks>
/// The provider here is a stand-in that answers from a script: the OpenAI adapters' own answer is pinned by the
/// shared tests of <c>Tracon.OpenAI.Shared</c>. What this class pins is the validator's side: which bindings it asks
/// about, with what, and what it makes of the answer.
/// </remarks>
public sealed class ResponseSchemaReportTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":"string"}}}""").RootElement.Clone();

    [Fact]
    public async Task The_report_carries_the_schema_the_provider_sends_for_the_binding()
    {
        var provider = new ShapingProvider("shaper", Shape(optional: [], keywords: [], strict: true));

        var report = await Validate(Definition(Format(), "shaper"), provider);

        var entry = report.ResponseSchemas.ShouldHaveSingleItem();
        entry.Provider.ShouldBe("shaper");
        entry.Model.ShouldBe("fake-model");
        entry.Strict.ShouldBeTrue();
        entry.Schema.GetRawText().ShouldBe("""{"sent":true}""");
        report.Messages.ShouldBeEmpty();
        report.Valid.ShouldBeTrue();
    }

    [Fact]
    public async Task The_provider_is_asked_with_the_binding_and_the_schema_as_the_author_wrote_them()
    {
        var provider = new ShapingProvider("shaper", Shape([], [], strict: false));
        var binding = TestData.Binding("shaper") with
        {
            ResponseFormat = Format(),
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["shaper.mode"] = JsonSerializer.SerializeToElement(true),
            },
        };

        await Validate(TestData.Definition() with { Model = binding }, provider);

        var call = provider.Calls.ShouldHaveSingleItem();
        call.Binding.ProviderSettings.ContainsKey("shaper.mode").ShouldBeTrue();
        call.Schema.GetRawText().ShouldBe(Schema.GetRawText());
    }

    [Fact]
    public async Task An_optional_property_sent_as_required_is_a_warning_that_names_the_properties()
    {
        var provider = new ShapingProvider("shaper", Shape(["#/properties/a", "#/properties/b"], [], strict: false));

        var report = await Validate(Definition(Format(), "shaper"), provider);

        var warning = report.Messages.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(ValidationSeverity.Warning);
        warning.Code.ShouldBe("response_schema_optional_required");
        warning.Path.ShouldBe("model.responseFormat.schema");
        warning.Message.ShouldContain("'shaper' provider");
        warning.Message.ShouldContain("#/properties/a, #/properties/b");
        warning.Message.ShouldContain("allow null");
        report.Valid.ShouldBeTrue("a warning never stops the definition");
    }

    [Fact]
    public async Task A_bound_that_is_not_sent_is_a_warning_that_names_the_bounds()
    {
        var provider = new ShapingProvider("shaper", Shape([], ["#/properties/a: maxLength"], strict: false));

        var report = await Validate(Definition(Format(), "shaper"), provider);

        var warning = report.Messages.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(ValidationSeverity.Warning);
        warning.Code.ShouldBe("response_schema_keyword_not_enforced");
        warning.Message.ShouldContain("#/properties/a: maxLength");
        warning.Message.ShouldContain("IStructuredResponseValidator");
    }

    [Fact]
    public async Task A_part_the_strict_mode_refuses_is_a_warning_that_names_the_keywords_and_does_not_stop_the_definition()
    {
        var provider = new ShapingProvider("shaper", Shape([], [], strict: true, refused: ["#/properties/a: oneOf"]));

        var report = await Validate(Definition(Format(), "shaper"), provider);

        var warning = report.Messages.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(ValidationSeverity.Warning, "an error would block saving a definition a tenant's own server can run");
        warning.Code.ShouldBe("response_schema_strict_unsupported");
        warning.Path.ShouldBe("model.responseFormat.schema");
        warning.Message.ShouldContain("'shaper' provider");
        warning.Message.ShouldContain("#/properties/a: oneOf");
        warning.Message.ShouldContain("anyOf");
        report.Valid.ShouldBeTrue();
        report.ResponseSchemas.ShouldHaveSingleItem("the entry shows the schema that was refused");
    }

    [Fact]
    public async Task The_same_schema_without_the_strict_mode_gives_no_such_warning()
    {
        var provider = new ShapingProvider("shaper", Shape([], [], strict: false, refused: ["#/properties/a: oneOf"]));

        var report = await Validate(Definition(Format(), "shaper"), provider);

        report.Messages.ShouldBeEmpty();
        report.Valid.ShouldBeTrue();
    }

    [Fact]
    public async Task Both_differences_give_both_warnings()
    {
        var provider = new ShapingProvider("shaper", Shape(["#/properties/a"], ["#/properties/a: maxLength"], strict: false));

        var report = await Validate(Definition(Format(), "shaper"), provider);

        report.Messages.Select(static message => message.Code).ShouldBe(
            ["response_schema_optional_required", "response_schema_keyword_not_enforced"]);
    }

    [Fact]
    public async Task A_long_list_is_cut_in_the_message_and_kept_whole_in_the_shape()
    {
        var many = Enumerable.Range(0, 11).Select(static index => $"#/properties/p{index}").ToArray();
        var provider = new ShapingProvider("shaper", Shape(many, [], strict: false));

        var report = await Validate(Definition(Format(), "shaper"), provider);

        var message = report.Messages.ShouldHaveSingleItem().Message;
        message.ShouldContain("#/properties/p7");
        message.ShouldNotContain("#/properties/p8");
        message.ShouldContain("and 3 more");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("text")]
    [InlineData("json")]
    public async Task A_definition_without_a_response_schema_adds_nothing_and_asks_nobody(string kind)
    {
        var provider = new ShapingProvider("shaper", Shape(["#/properties/a"], ["#: not"], strict: false));
        var format = kind switch
        {
            "text" => new AgentResponseFormat { Kind = AgentResponseFormatKind.Text },
            "json" => new AgentResponseFormat { Kind = AgentResponseFormatKind.Json },
            _ => null,
        };

        var report = await Validate(Definition(format, "shaper"), provider);

        report.ResponseSchemas.ShouldBeEmpty();
        report.Messages.ShouldBeEmpty();
        provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_provider_that_does_not_state_its_schema_adds_nothing()
    {
        var report = await Validate(
            Definition(Format(), "fake"),
            new FakeModelProvider(models: [new ModelDescriptor { Name = "fake-model", SupportsStructuredOutput = true }]));

        report.ResponseSchemas.ShouldBeEmpty();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_fallback_link_is_described_with_a_binding_that_carries_no_provider_settings()
    {
        var primary = new ShapingProvider("shaper", Shape(["#/properties/a"], [], strict: true));
        var backup = new ShapingProvider("backup", Shape(["#/properties/a"], [], strict: false));
        var binding = TestData.Binding("shaper") with
        {
            ResponseFormat = Format(),
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["shaper.mode"] = JsonSerializer.SerializeToElement(true),
            },
            Fallbacks = [new ModelFallback { Provider = "backup", Model = "backup-model" }],
        };

        var report = await Validate(TestData.Definition() with { Model = binding }, primary, backup);

        report.ResponseSchemas.Select(static entry => (entry.Provider, entry.Model, entry.Strict)).ShouldBe(
        [
            ("shaper", "fake-model", true),
            ("backup", "backup-model", false),
        ]);

        var link = backup.Calls.ShouldHaveSingleItem().Binding;
        link.Provider.ShouldBe("backup");
        link.Model.ShouldBe("backup-model");
        link.ProviderSettings.ShouldBeEmpty("a link receives none of the primary binding's provider settings");

        report.Messages.Select(static message => (message.Code, message.Path)).ShouldBe(
        [
            ("response_schema_optional_required", "model.responseFormat.schema"),
            ("response_schema_optional_required", "model.fallbacks[0]"),
        ]);
    }

    [Fact]
    public async Task Two_links_to_one_provider_give_two_entries_and_one_warning()
    {
        var primary = new ShapingProvider("shaper", Shape(["#/properties/a"], [], strict: false));
        var binding = TestData.Binding("shaper") with
        {
            ResponseFormat = Format(),
            Fallbacks =
            [
                new ModelFallback { Provider = "shaper", Model = "m1" },
                new ModelFallback { Provider = "shaper", Model = "m2" },
            ],
        };

        var report = await Validate(TestData.Definition() with { Model = binding }, primary);

        report.ResponseSchemas.Count.ShouldBe(3);
        report.Messages.ShouldHaveSingleItem().Code.ShouldBe("response_schema_optional_required");
    }

    [Fact]
    public async Task A_provider_that_cannot_describe_the_schema_adds_no_entry_and_does_not_break_the_report()
    {
        var provider = new ShapingProvider("shaper", null) { Throws = NotASchema(Schema) };

        var report = await Validate(Definition(Format(), "shaper"), provider);

        report.ResponseSchemas.ShouldBeEmpty();
        report.Messages.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("invalid-operation")]
    [InlineData("format")]
    public async Task Whatever_the_provider_throws_while_describing_the_report_is_still_returned(string kind)
    {
        Exception thrown = string.Equals(kind, "format", StringComparison.Ordinal)
            ? new FormatException("bad")
            : new InvalidOperationException("not text");
        var provider = new ShapingProvider("shaper", null) { Throws = thrown };

        var report = await Validate(Definition(Format(), "shaper"), provider);

        report.ResponseSchemas.ShouldBeEmpty();
        report.Valid.ShouldBeTrue();
    }

    [Fact]
    public async Task A_provider_setting_of_the_wrong_type_adds_no_entry()
    {
        var provider = new ShapingProvider("shaper", null) { Throws = new TraconException("wrong type") };

        var report = await Validate(Definition(Format(), "shaper"), provider);

        report.ResponseSchemas.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unregistered_provider_adds_no_entry()
    {
        var report = await Validate(Definition(Format(), "nobody"), new ShapingProvider("shaper", Shape([], [], strict: false)));

        report.ResponseSchemas.ShouldBeEmpty();
        report.Messages.ShouldContain(static message => message.Code == "unknown_model");
    }

    [Fact]
    public void The_registry_hands_the_provider_to_the_descriptor()
    {
        var shaper = new ShapingProvider("shaper", Shape([], [], strict: false));
        var registry = TestData.Providers(shaper, new FakeModelProvider());

        var descriptors = registry.List().ToDictionary(static descriptor => descriptor.Name, StringComparer.Ordinal);

        descriptors["shaper"].ResponseSchemaSupport.ShouldBeSameAs(shaper);
        descriptors["fake"].ResponseSchemaSupport.ShouldBeNull();
    }

    private static ArgumentException NotASchema(JsonElement schema) => new("not a schema", nameof(schema));

    private static AgentResponseFormat Format() => new() { Kind = AgentResponseFormatKind.JsonSchema, Schema = Schema };

    private static AgentDefinition Definition(AgentResponseFormat? format, string provider)
        => TestData.Definition() with { Model = TestData.Binding(provider) with { ResponseFormat = format } };

    private static ResponseSchemaShape Shape(
        IReadOnlyList<string> optional,
        IReadOnlyList<string> keywords,
        bool strict,
        IReadOnlyList<string>? refused = null)
        => new()
        {
            EffectiveSchema = JsonDocument.Parse("""{"sent":true}""").RootElement.Clone(),
            Strict = strict,
            OptionalMadeRequired = optional,
            KeywordsNotEnforced = keywords,
            StrictUnsupported = refused ?? [],
        };

    private static async Task<AgentValidationReport> Validate(AgentDefinition definition, params IModelProvider[] providers)
    {
        var models = TestData.Providers(providers);
        var tools = TestData.Registry();
        var catalog = new FakeAgentCatalog([]);
        var options = Options.Create(new TraconOptions());
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), new SingleTenantContext(options), options);
        var compiler = new AgentDefinitionCompiler(models, tools, skills: skills, callableAgents: new CallableAgentResolver(catalog));

        return await new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options)
            .ValidateAsync(definition, TestContext.Current.CancellationToken);
    }

    /// <summary>A provider that answers <see cref="IResponseSchemaSupport"/> from a script and keeps the calls.</summary>
    private sealed class ShapingProvider(string name, ResponseSchemaShape? shape) : IModelProvider, IResponseSchemaSupport
    {
        public List<(ModelBinding Binding, JsonElement Schema)> Calls { get; } = [];

        public Exception? Throws { get; init; }

        public string Name => name;

        public IReadOnlyList<ModelDescriptor> Models { get; } =
        [
            new ModelDescriptor { Name = "fake-model", SupportsStructuredOutput = true },
            new ModelDescriptor { Name = "backup-model", SupportsStructuredOutput = true },
            new ModelDescriptor { Name = "m1", SupportsStructuredOutput = true },
            new ModelDescriptor { Name = "m2", SupportsStructuredOutput = true },
        ];

        public IChatClient CreateChatClient(ModelBinding binding) => new FakeChatClient();

        public ResponseSchemaShape DescribeResponseSchema(ModelBinding binding, JsonElement schema)
        {
            Calls.Add((binding, schema));

            return Throws is null ? shape! : throw Throws;
        }
    }
}
