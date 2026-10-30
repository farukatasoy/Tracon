namespace Tracon.Core.UnitTests.Workspace;

/// <summary>
/// The shape of the paths of the session workspace. The shape is permanent: a workspace written under
/// one shape is not found under another.
/// </summary>
public sealed class SessionWorkspacePathsTests
{
    [Fact]
    public void The_key_of_a_session_is_32_lower_case_hexadecimal_characters()
    {
        var key = SessionWorkspacePaths.Key("acme", "session-1");

        key.Length.ShouldBe(32);
        key.ShouldBe(key.ToLowerInvariant());
        key.All(static character => char.IsAsciiHexDigit(character)).ShouldBeTrue();
    }

    [Fact]
    public void The_key_of_a_session_is_pinned()
    {
        // A change of this value moves every workspace that was written before it. If it must change, move the files.
        SessionWorkspacePaths.Key("acme", "session-1").ShouldBe(PinnedKey);
    }

    [Fact]
    public void A_tenant_in_another_letter_case_is_the_same_tenant()
    {
        SessionWorkspacePaths.Key("ACME", "session-1").ShouldBe(SessionWorkspacePaths.Key("acme", "session-1"));
        SessionWorkspacePaths.TenantRoot("ACME").ShouldBe("acme/~workspace");
    }

    [Fact]
    public void Another_session_or_another_tenant_has_another_key()
    {
        var key = SessionWorkspacePaths.Key("acme", "session-1");

        Differ(SessionWorkspacePaths.Key("acme", "session-2"), key);
        Differ(SessionWorkspacePaths.Key("other", "session-1"), key);
        Differ(SessionWorkspacePaths.Key("acme", "SESSION-1"), key);
    }

    [Fact]
    public void Each_part_of_the_key_input_carries_its_length_so_two_pairs_never_make_the_same_input()
    {
        Differ(SessionWorkspacePaths.Key("ab", "c"), SessionWorkspacePaths.Key("a", "bc"));
        Differ(SessionWorkspacePaths.Key("a", "bc"), SessionWorkspacePaths.Key("abc", "x"));
    }

    [Fact]
    public void The_session_id_is_not_in_the_path()
    {
        SessionWorkspacePaths.AgentRoot("acme", "session-1", "drafter").ShouldBe($"acme/~workspace/{PinnedKey}/drafter");
        SessionWorkspacePaths.AgentRoot("acme", "session-1", "drafter").ShouldNotContain("session-1");
    }

    [Fact]
    public void The_workspace_of_every_agent_in_a_session_is_below_one_directory()
    {
        SessionWorkspacePaths.AgentRoot("acme", "session-1", "a").ShouldStartWith(SessionWorkspacePaths.SessionRoot("acme", "session-1") + "/");
        SessionWorkspacePaths.AgentRoot("acme", "session-1", "b").ShouldStartWith(SessionWorkspacePaths.SessionRoot("acme", "session-1") + "/");
        SessionWorkspacePaths.SessionRoot("acme", "session-1").ShouldStartWith(SessionWorkspacePaths.TenantRoot("acme") + "/");
    }

    [Theory]
    [InlineData("notes.md", "notes.md")]
    [InlineData("/notes.md", "notes.md")]
    [InlineData("reports/2026/q3.md", "reports/2026/q3.md")]
    [InlineData("reports\\2026\\q3.md", "reports/2026/q3.md")]
    [InlineData("reports//q3.md/", "reports/q3.md")]
    [InlineData("", "")]
    [InlineData("/", "")]
    public void A_path_is_cleaned_to_a_path_below_the_root(string path, string expected)
        => SessionWorkspacePaths.Relative(path).ShouldBe(expected);

    [Theory]
    [InlineData("..")]
    [InlineData("../other/notes.md")]
    [InlineData("reports/../../other.md")]
    [InlineData("reports/./q3.md")]
    [InlineData("reports\\..\\..\\other.md")]
    public void A_path_that_leaves_the_workspace_is_refused(string path)
        => Should.Throw<TraconException>(() => SessionWorkspacePaths.Relative(path)).Message.ShouldContain("'.' or '..'");

    [Fact]
    public void A_path_with_a_control_character_is_refused()
        => Should.Throw<TraconException>(() => SessionWorkspacePaths.Relative("notes\0.md")).Message.ShouldContain("control characters");

    private static void Differ(string actual, string other)
        => string.Equals(actual, other, StringComparison.Ordinal).ShouldBeFalse();

    // SHA-256 of 4-byte little-endian length + UTF-8 bytes, per part (tenant, session); first 32 hex characters. Cross-checked with an independent implementation.
    private const string PinnedKey = "ca623ece274a2fc48bb3e64ccb1c2551";
}
