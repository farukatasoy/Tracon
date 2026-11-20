using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <see cref="IAgentDefinitionValidator"/> from the container of a real host, beside <c>POST /api/agents/validate</c>.
/// </summary>
/// <remarks>
/// A host that writes definitions from its own code asks the container for the validator and learns what the HTTP
/// writers learn before they save. The two must say the same thing: one report type, one set of codes.
/// </remarks>
public sealed class DefinitionValidatorSurfaceTests
{
    private const string ProviderName = "surface-provider";
    private const string ModelName = "surface-model";

    [Fact]
    public async Task The_validator_in_the_container_reports_the_findings_the_http_endpoint_reports()
    {
        await using var host = await StartAsync();

        // A model nobody registered, a tool nobody registered, and a parameter written in single braces.
        var definition = new AgentDefinition
        {
            Name = "surface-agent",
            Instructions = "Greet {customer}.",
            Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "a customer" }],
            ToolNames = ["not-a-tool"],
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
        };

        var fromCode = await host.Services.GetRequiredService<IAgentDefinitionValidator>()
            .ValidateAsync(definition, TestContext.Current.CancellationToken);

        fromCode.Valid.ShouldBeFalse();
        fromCode.Messages.Select(static message => message.Code).ShouldBe(["unknown_tool", "single_brace_placeholder"], ignoreOrder: true);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new
            {
                name = definition.Name,
                instructions = definition.Instructions,
                parameters = new[] { new { name = "customer", kind = "Text", defaultValue = "a customer" } },
                toolNames = definition.ToolNames,
                model = new { provider = ProviderName, model = ModelName },
            },
            TestContext.Current.CancellationToken);

        var body = await TraconTestHost.ReadJsonAsync(response);
        var overHttp = body.GetProperty("messages").EnumerateArray().Select(static message => message.GetProperty("code").GetString()).ToList();

        overHttp.ShouldBe(fromCode.Messages.Select(static message => message.Code).ToList(), ignoreOrder: true);
        body.GetProperty("valid").GetBoolean().ShouldBe(fromCode.Valid);
    }

    [Fact]
    public async Task A_definition_with_a_warning_only_is_valid_and_a_blank_one_is_refused_before_the_check()
    {
        await using var host = await StartAsync();
        var validator = host.Services.GetRequiredService<IAgentDefinitionValidator>();

        var report = await validator.ValidateAsync(
            new AgentDefinition
            {
                Name = "warned",
                Instructions = "Greet {customer}.",
                Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "a customer" }],
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
            },
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldHaveSingleItem().Severity.ShouldBe(ValidationSeverity.Warning);

        await Should.ThrowAsync<ArgumentNullException>(async () => await validator.ValidateAsync(null!, TestContext.Current.CancellationToken));
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(builder => builder.AddModelProvider(
            new FakeModelProvider(ProviderName).WithModel(new ModelDescriptor { Name = ModelName }).EchoesUserMessage()));
}
