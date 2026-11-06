using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

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

    [Fact]
    public void The_entry_a_culture_selects_is_named_as_the_dictionary_spells_it()
    {
        var definition = WithCultures(("en", "Hello."), ("TR", "Merhaba."), ("zh-Hans", "Nihao."));

        InstructionCultureResolver.Canonical(definition, "tr").ShouldBe("TR");
        InstructionCultureResolver.Canonical(definition, "tr-TR").ShouldBe("TR");
        InstructionCultureResolver.Canonical(definition, "ZH-hans").ShouldBe("zh-Hans");
        InstructionCultureResolver.Canonical(definition, "en").ShouldBe("en");
    }

    [Fact]
    public void A_culture_that_selects_no_entry_has_the_empty_name()
    {
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        foreach (var culture in new[] { null, string.Empty, " ", "de", "zh-Hans-CN", "-tr", "tr_TR" })
        {
            InstructionCultureResolver.Canonical(definition, culture).ShouldBe(string.Empty, $"culture '{culture}'");
        }

        InstructionCultureResolver.Canonical(TestData.Definition("a"), "tr").ShouldBe(string.Empty, "no dictionary");
    }

    /// <summary>
    /// The cache keys on the name and compiles from it, so the name must select exactly what the request did, for
    /// any text a request body can carry. Cultures are generated, not listed, because the cases that break an
    /// equivalence like this one are the ones nobody thought to write down.
    /// </summary>
    [Fact]
    public void Resolving_the_name_of_a_culture_gives_the_text_the_culture_gave()
    {
        var definitions = new[]
        {
            TestData.Definition("none"),
            WithCultures(("en", "Hello."), ("tr", "Merhaba.")),
            WithCultures(("en", "Hello."), ("TR", "Merhaba."), ("zh-Hans", "Nihao."), ("pt-BR", "Ola.")),
            WithCultures((" ", "Blank."), ("tr", "Merhaba.")),
            WithCultures(("tr", "Lower."), ("TR", "Upper.")),
            WithCultures(("a-b", "Hyphenated."), ("a", "Plain.")),
        };

        var random = new Random(306);
        var alphabet = "trTRenzhxa- _İı\t".ToCharArray();
        var cultures = new List<string?> { null, string.Empty, " ", "\t", "-", "--", "tr-", " -x", "\t-tr", "tr tr", "tür-TR", "ı-x", "İ" };
        cultures.Add(new string('a', 10_000));
        cultures.Add("tr-" + new string('x', 10_000));
        cultures.Add(new string('-', 10_000));

        for (var index = 0; index < 3_000; index++)
        {
            var length = random.Next(0, 9);
            cultures.Add(string.Concat(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)])));
        }

        foreach (var (position, definition) in definitions.Index())
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var culture in cultures)
            {
                var name = InstructionCultureResolver.Canonical(definition, culture);

                InstructionCultureResolver.Resolve(definition, name)
                    .ShouldBe(InstructionCultureResolver.Resolve(definition, culture), $"definition #{position}, culture '{culture}'");
                InstructionCultureResolver.Canonical(definition, name)
                    .ShouldBe(name, $"definition #{position}, culture '{culture}': naming a name changes nothing");

                names.Add(name);
            }

            var entries = definition.InstructionsByCulture?.Count ?? 0;
            names.Count.ShouldBeLessThanOrEqualTo(entries + 1, $"definition #{position}: a slot per entry and one for the default");
        }
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
