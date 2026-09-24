using System.Reflection;

namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// The content fingerprints a script grant pins. A field that changes what runs
/// and does not reach the hash would let a pinned grant authorize different
/// content without anyone noticing.
/// </summary>
public sealed class SkillScriptHashTests
{
    /// <summary>
    /// Structural guard: every property of the script definition either changes
    /// the hash or is on the justified exclusion list. A property added later fails
    /// here until someone decides which side it belongs to.
    /// </summary>
    [Fact]
    public void Every_property_is_hashed_or_explicitly_excluded()
    {
        var baseline = Script();

        // Excluded on purpose: Description reaches only the model, Name is the
        // grant's key (it enters the set fingerprint instead), and ContentHash is
        // the hash itself.
        string[] excluded =
        [
            nameof(AgentSkillScriptDefinition.Description),
            nameof(AgentSkillScriptDefinition.Name),
            nameof(AgentSkillScriptDefinition.ContentHash),
        ];

        var mutations = Mutations(baseline).ToList();

        var properties = typeof(AgentSkillScriptDefinition)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        properties.ShouldBe(
            [.. mutations.Select(static mutation => mutation.Field).Concat(excluded).Order(StringComparer.Ordinal)],
            "A property of AgentSkillScriptDefinition is neither hashed nor excluded; decide which, here and in SkillScriptHashing.");

        foreach (var (field, mutated) in mutations)
        {
            mutated.ContentHash.ShouldNotBe(baseline.ContentHash, StringComparer.Ordinal, $"changing {field} left the hash unchanged");
        }

        foreach (var field in excluded.Where(static field => !string.Equals(field, nameof(AgentSkillScriptDefinition.ContentHash), StringComparison.Ordinal)))
        {
            var changed = string.Equals(field, nameof(AgentSkillScriptDefinition.Name), StringComparison.Ordinal)
                ? baseline with { Name = "other" }
                : baseline with { Description = "Something else." };

            changed.ContentHash.ShouldBe(baseline.ContentHash, $"{field} is excluded but changed the hash");
        }
    }

    [Fact]
    public void The_hash_is_sixty_four_upper_case_hexadecimal_characters()
    {
        var hash = Script().ContentHash;

        hash.Length.ShouldBe(64);
        hash.ShouldAllBe(static character => char.IsAsciiHexDigitUpper(character) || char.IsAsciiDigit(character));
        SkillScriptHashing.IsWellFormed(hash).ShouldBeTrue();
        SkillScriptHashing.IsWellFormed(hash.ToLowerInvariant()).ShouldBeTrue();
        SkillScriptHashing.IsWellFormed(hash[..63]).ShouldBeFalse();
        SkillScriptHashing.IsWellFormed(hash[..63] + "G").ShouldBeFalse();
        SkillScriptHashing.IsWellFormed(null).ShouldBeFalse();
    }

    /// <summary>
    /// Fields are length-prefixed, so moving characters across a field boundary
    /// changes the hash. A separator-joined format would hash these two alike.
    /// </summary>
    [Fact]
    public void Moving_text_across_a_field_boundary_changes_the_hash()
    {
        var left = SkillScriptHashing.ComputeScriptHash("sh", "echo a", "b");
        var right = SkillScriptHashing.ComputeScriptHash("sh", "echo ", "ab");

        left.ShouldNotBe(right, StringComparer.Ordinal);
        SkillScriptHashing.ComputeScriptHash("s", "hecho", null)
            .ShouldNotBe(SkillScriptHashing.ComputeScriptHash("sh", "echo", null), StringComparer.Ordinal);
    }

    /// <summary>The runner treats a null schema and an empty one the same, so the hash does too.</summary>
    [Fact]
    public void A_null_schema_and_an_empty_schema_hash_the_same()
    {
        (Script() with { ParametersSchema = null }).ContentHash
            .ShouldBe((Script() with { ParametersSchema = string.Empty }).ContentHash);
    }

    /// <summary>The runner strips a leading dot from the extension; the hash follows it.</summary>
    [Fact]
    public void A_leading_dot_on_the_extension_does_not_change_the_hash_but_its_case_does()
    {
        (Script() with { Extension = ".sh" }).ContentHash.ShouldBe((Script() with { Extension = "sh" }).ContentHash);
        (Script() with { Extension = "SH" }).ContentHash.ShouldNotBe((Script() with { Extension = "sh" }).ContentHash, StringComparer.Ordinal);
    }

    [Fact]
    public void The_set_fingerprint_does_not_depend_on_script_order()
    {
        var first = Script() with { Name = "first" };
        var second = Script() with { Name = "second", Content = "echo two" };

        Skill(first, second).ScriptSetHash.ShouldBe(Skill(second, first).ScriptSetHash);
    }

    [Fact]
    public void Adding_removing_renaming_or_changing_a_script_changes_the_set_fingerprint()
    {
        var first = Script() with { Name = "first" };
        var second = Script() with { Name = "second", Content = "echo two" };
        var baseline = Skill(first, second).ScriptSetHash;

        Skill(first).ScriptSetHash.ShouldNotBe(baseline, StringComparer.Ordinal, "removing a script");
        Skill(first, second, Script() with { Name = "third" }).ScriptSetHash.ShouldNotBe(baseline, StringComparer.Ordinal, "adding a script");
        Skill(first, second with { Name = "renamed" }).ScriptSetHash.ShouldNotBe(baseline, StringComparer.Ordinal, "renaming a script");
        Skill(first, second with { Content = "echo changed" }).ScriptSetHash.ShouldNotBe(baseline, StringComparer.Ordinal, "changing a script");
    }

    /// <summary>
    /// A skill with no scripts still has a fingerprint, so a skill-wide grant given
    /// while it was empty stops authorizing the moment a script is added.
    /// </summary>
    [Fact]
    public void An_empty_script_set_has_a_fingerprint_that_the_first_script_changes()
    {
        var empty = Skill().ScriptSetHash;

        SkillScriptHashing.IsWellFormed(empty).ShouldBeTrue();
        Skill(Script()).ScriptSetHash.ShouldNotBe(empty, StringComparer.Ordinal);
    }

    /// <summary>Domain separation: a single script's hash never equals a set fingerprint.</summary>
    [Fact]
    public void A_script_hash_never_equals_the_set_fingerprint()
    {
        var script = Script();

        Skill(script).ScriptSetHash.ShouldNotBe(script.ContentHash, StringComparer.Ordinal);
    }

    [Fact]
    public void The_hashes_are_computed_not_stored_and_do_not_enter_record_equality()
    {
        // Two equal records have equal hashes; a record built with `with` recomputes.
        var script = Script();
        (script with { }).ShouldBe(script);
        (script with { Content = "echo other" }).ContentHash.ShouldNotBe(script.ContentHash, StringComparer.Ordinal);

        typeof(AgentSkillScriptDefinition).GetProperty(nameof(AgentSkillScriptDefinition.ContentHash))!.CanWrite.ShouldBeFalse();
        typeof(AgentSkillDefinition).GetProperty(nameof(AgentSkillDefinition.ScriptSetHash))!.CanWrite.ShouldBeFalse();
    }

    private static IEnumerable<(string Field, AgentSkillScriptDefinition Script)> Mutations(AgentSkillScriptDefinition baseline)
    {
        yield return (nameof(AgentSkillScriptDefinition.Extension), baseline with { Extension = "py" });
        yield return (nameof(AgentSkillScriptDefinition.Content), baseline with { Content = "echo other" });
        yield return (nameof(AgentSkillScriptDefinition.ParametersSchema), baseline with { ParametersSchema = """{"type":"object"}""" });
    }

    private static AgentSkillScriptDefinition Script() => new()
    {
        Name = "hello",
        Description = "Says hello.",
        Extension = "sh",
        Content = "echo hello",
        ParametersSchema = null,
    };

    private static AgentSkillDefinition Skill(params AgentSkillScriptDefinition[] scripts) => new()
    {
        TenantId = "default",
        Name = "hash-skill",
        Description = "Hash test skill.",
        Instructions = "None.",
        Scripts = scripts,
    };
}
