using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class InstructionCultureResolutionTests
{
    [Fact]
    public void Empty_culture_dictionary_returns_the_default_instructions()
    {
        var definition = TestData.Definition("a");

        InstructionCultureResolver.Resolve(definition, "tr").ShouldBe(definition.Instructions);
    }

    [Fact]
    public void Null_requested_culture_returns_the_default_instructions()
    {
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        InstructionCultureResolver.Resolve(definition, null).ShouldBe(definition.Instructions);
    }

    [Fact]
    public void Exact_match_is_used()
    {
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        InstructionCultureResolver.Resolve(definition, "tr").ShouldBe("Merhaba.");
    }

    [Fact]
    public void Region_subtag_falls_back_to_its_parent()
    {
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        InstructionCultureResolver.Resolve(definition, "tr-TR").ShouldBe("Merhaba.");
    }

    [Fact]
    public void Unmatched_culture_falls_back_to_the_default_instructions_without_throwing()
    {
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        InstructionCultureResolver.Resolve(definition, "de").ShouldBe(definition.Instructions);
    }

    [Fact]
    public void Match_is_case_insensitive()
    {
        var definition = WithCultures(("en", "Hello."), ("TR", "Merhaba."));

        InstructionCultureResolver.Resolve(definition, "tr").ShouldBe("Merhaba.");
    }

    private static AgentDefinition WithCultures(params (string Culture, string Text)[] entries)
        => TestData.Definition("a") with
        {
            InstructionsByCulture = entries.ToDictionary(
                static entry => entry.Culture,
                static entry => entry.Text,
                StringComparer.Ordinal),
        };
}
