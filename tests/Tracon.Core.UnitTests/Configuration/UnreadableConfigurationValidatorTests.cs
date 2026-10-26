using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The rule that every text a binder cannot read stops the host at startup (F-572),
/// across the scalar types and across the list keys (F-508).
/// </summary>
/// <remarks>
/// <para>
/// Before the rule, a number or flag the binder could not read left the shipped
/// default in place and said nothing: <c>DefaultMaxOutputBytes = "10MB"</c> kept the
/// tool output unbounded, <c>RecordToolPayloads = "nope"</c> kept recording, and
/// <c>RateLimit:Enabled = "evet"</c> kept the limit off. The default of each of these
/// is the loose side, so a typo opened a limit or a protection.
/// </para>
/// <para>
/// Every case goes through <see cref="SectionBinding"/>: the container a host builds,
/// <c>IStartupValidator</c> as the startup path. A validator tested by calling it
/// directly would not prove the host refuses to start.
/// </para>
/// </remarks>
public sealed class UnreadableConfigurationValidatorTests
{
    [Fact]
    public void An_unreadable_number_names_the_key_and_the_text()
    {
        var failure = SectionBinding.FailsAtStartup(Keys(("Tracon:Tools:DefaultMaxOutputBytes", "10MB")));

        failure.Message.ShouldContain("Tracon:Tools:DefaultMaxOutputBytes is '10MB'");
        failure.Message.ShouldContain("32-bit whole number");
    }

    [Fact]
    public void Every_unreadable_value_of_one_options_type_is_named_in_one_failure()
    {
        // Three sub-sections of the one TraconOptions: the operator fixes all three from one start.
        var failure = SectionBinding.FailsAtStartup(Keys(
            ("Tracon:Tools:DefaultMaxOutputBytes", "10MB"),
            ("Tracon:RunRecording:RecordToolPayloads", "nope"),
            ("Tracon:Observability:SuccessSampleRatio", "half")));

        failure.Message.ShouldContain("Tracon:Tools:DefaultMaxOutputBytes is '10MB'");
        failure.Message.ShouldContain("Tracon:RunRecording:RecordToolPayloads is 'nope'");
        failure.Message.ShouldContain("Tracon:Observability:SuccessSampleRatio is 'half'");
    }

    [Fact]
    public void Unreadable_values_of_two_options_types_are_both_reported()
    {
        // TraconOptions and TraconRateLimitOptions are two registrations; the host reports both of them.
        var messages = AllStartupMessages(Keys(
            ("Tracon:Tools:DefaultMaxOutputBytes", "10MB"),
            ("Tracon:RateLimit:Enabled", "evet")));

        messages.ShouldContain(static message => message.Contains("Tracon:Tools:DefaultMaxOutputBytes is '10MB'", StringComparison.Ordinal));
        messages.ShouldContain(static message => message.Contains("Tracon:RateLimit:Enabled is 'evet'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Tracon:RateLimit:Enabled", "evet", "true or false")]
    [InlineData("Tracon:Webhooks:Timeout", "30s", "time span")]
    [InlineData("Tracon:RateLimit:Partition", "per-user", "one of")]
    [InlineData("Tracon:AgentGraph:MaxTotalCost", "1,5", "decimal separator")]
    [InlineData("Tracon:Attachments:MaxBytes", "99999999999999999999", "64-bit whole number")]
    [InlineData("Tracon:MaxParameterValueLength", "3000000000", "32-bit whole number")]
    [InlineData("Tracon:Preflight:ReserveRatio", "0,25", "a number")]
    public void Each_scalar_type_refuses_text_it_cannot_read(string key, string text, string expected)
    {
        var failure = SectionBinding.FailsAtStartup(Keys((key, text)));

        failure.Message.ShouldContain($"{key} is '{text}'");
        failure.Message.ShouldContain(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_or_blank_value_means_not_given(string text)
    {
        var values = Keys(
            ("Tracon:Tools:DefaultMaxOutputBytes", text),
            ("Tracon:RateLimit:Enabled", text),
            ("Tracon:Webhooks:Timeout", text),
            ("Tracon:RateLimit:Partition", text),
            ("Tracon:AgentGraph:MaxTotalCost", text),
            ("Tracon:Attachments:MaxBytes", text));

        SectionBinding.StartsCleanly(values);

        var options = SectionBinding.Bind<TraconOptions>(values);

        options.Tools.DefaultMaxOutputBytes.ShouldBe(new TraconToolOptions().DefaultMaxOutputBytes);
        options.Attachments.MaxBytes.ShouldBe(new TraconAttachmentOptions().MaxBytes);
        SectionBinding.Bind<TraconRateLimitOptions>(values).Enabled.ShouldBe(new TraconRateLimitOptions().Enabled);
    }

    [Fact]
    public void A_value_with_surrounding_white_space_is_still_readable()
    {
        var values = Keys(("Tracon:Tools:DefaultMaxOutputBytes", " 2048 "), ("Tracon:RateLimit:Enabled", " true "));

        SectionBinding.StartsCleanly(values);

        SectionBinding.Bind<TraconOptions>(values).Tools.DefaultMaxOutputBytes.ShouldBe(2048);
        SectionBinding.Bind<TraconRateLimitOptions>(values).Enabled.ShouldBeTrue();
    }

    [Fact]
    public void Code_that_assigns_the_option_after_binding_wins_over_an_unreadable_value()
    {
        // K-993: the failure holds while the option still has what the binder left.
        var values = Keys(("Tracon:Tools:DefaultMaxOutputBytes", "10MB"), ("Tracon:RateLimit:Enabled", "evet"));

        SectionBinding.StartsCleanly(values, static (builder, _) =>
        {
            builder.Services.Configure<TraconOptions>(static options => options.Tools.DefaultMaxOutputBytes = 4096);
            builder.Services.Configure<TraconRateLimitOptions>(static options => options.Enabled = true);
        });
    }

    [Fact]
    public void Code_that_assigns_one_option_does_not_hide_another_unreadable_value()
    {
        var values = Keys(("Tracon:Tools:DefaultMaxOutputBytes", "10MB"), ("Tracon:RunRecording:RecordToolPayloads", "nope"));

        var failure = SectionBinding.FailsAtStartup(values, static (builder, _) =>
            builder.Services.Configure<TraconOptions>(static options => options.Tools.DefaultMaxOutputBytes = 4096));

        failure.Message.ShouldNotContain("DefaultMaxOutputBytes");
        failure.Message.ShouldContain("Tracon:RunRecording:RecordToolPayloads is 'nope'");
    }

    [Fact]
    public void Code_that_writes_the_default_value_still_fails()
    {
        // The accepted limit of the rule: a value equal to what the binder left cannot be told apart
        // from "the binder left it".
        var values = Keys(("Tracon:RateLimit:Enabled", "evet"));

        var failure = SectionBinding.FailsAtStartup(values, static (builder, _) =>
            builder.Services.Configure<TraconRateLimitOptions>(static options => options.Enabled = new TraconRateLimitOptions().Enabled));

        failure.Message.ShouldContain("Tracon:RateLimit:Enabled is 'evet'");
    }

    [Fact]
    public void A_text_that_is_long_or_has_control_characters_is_cut_in_the_message()
    {
        var failure = SectionBinding.FailsAtStartup(Keys(("Tracon:Tools:DefaultMaxOutputBytes", "line one\nline two " + new string('x', 200))));

        failure.Message.ShouldContain("'line one line two ");
        failure.Message.ShouldContain("…'");
        failure.Message.ShouldNotContain('\n');
        failure.Message.Length.ShouldBeLessThan(400);
    }

    [Fact]
    public void An_unreadable_value_of_a_nested_options_object_is_named_by_its_full_key()
    {
        var failure = SectionBinding.FailsAtStartup(Keys(("Tracon:Retention:RunEvents:MaxAgeDays", "thirty")));

        failure.Message.ShouldContain("Tracon:Retention:RunEvents:MaxAgeDays is 'thirty'");
    }

    [Fact]
    public void A_valid_configuration_starts_cleanly()
    {
        SectionBinding.StartsCleanly(Keys(
            ("Tracon:Tools:DefaultMaxOutputBytes", "4096"),
            ("Tracon:RateLimit:Enabled", "true"),
            ("Tracon:Webhooks:Timeout", "00:00:30"),
            ("Tracon:RateLimit:Partition", "Global")));
    }

    [Theory]
    [InlineData("Tracon:Skills:Scripts:EnvironmentAllowList", "PATH")]
    [InlineData("Tracon:Scheduling:HttpSchedulableHandlerKeys", "contoso.nightly-report")]
    [InlineData("Tracon:Scheduling:Lanes", "media")]
    [InlineData("Tracon:ContentGuard:Pattern:DeniedTerms", "project-falcon")]
    [InlineData("Tracon:OnlineEvaluation:AgentNames", "support")]
    [InlineData("Tracon:Attachments:AllowedMediaTypes", "image/png")]
    [InlineData("Tracon:Quotas:ThresholdPercents", "80")]
    [InlineData("Tracon:Webhooks:RetryDelays", "00:01:00")]
    [InlineData("Tracon:ContentProtection:Columns", "RunInput")]
    public void A_list_key_that_holds_one_value_is_refused_and_the_message_shows_the_entry_form(string key, string text)
    {
        // F-508: the binder reads entries (key:0, key:1). One value on the key itself used to be ignored
        // without a word, so "EnvironmentAllowList=PATH" kept the shipped list.
        var failure = SectionBinding.FailsAtStartup(Keys((key, text)));

        failure.Message.ShouldContain($"{key} is '{text}'");
        failure.Message.ShouldContain($"write it as {key}:0");
    }

    [Theory]
    [InlineData("Tracon:Skills:Scripts:EnvironmentAllowList")]
    [InlineData("Tracon:Scheduling:Lanes")]
    [InlineData("Tracon:ContentGuard:Pattern:DeniedTerms")]
    [InlineData("Tracon:Quotas:ThresholdPercents")]
    [InlineData("Tracon:Webhooks:RetryDelays")]
    [InlineData("Tracon:ContentProtection:Columns")]
    public void A_list_key_with_no_text_keeps_the_default(string key)
    {
        // A JSON "[]" reads as a key with no children in every configuration package version; it is
        // documented as "not given", not as "empty the list".
        SectionBinding.StartsCleanly(Keys((key, string.Empty)));
        SectionBinding.StartsCleanly(Keys((key, "  ")));
    }

    [Fact]
    public void A_list_written_as_entries_replaces_the_default()
    {
        var values = Keys(
            ("Tracon:Skills:Scripts:EnvironmentAllowList:0", "PATH"),
            ("Tracon:Skills:Scripts:EnvironmentAllowList:1", "HOME"));

        SectionBinding.StartsCleanly(values);

        SectionBinding.Bind<TraconOptions>(values).Skills.Scripts.EnvironmentAllowList.ShouldBe(["PATH", "HOME"]);
    }

    [Fact]
    public void Code_that_replaces_a_list_wins_over_a_single_value()
    {
        var values = Keys(("Tracon:Skills:Scripts:EnvironmentAllowList", "PATH"));

        SectionBinding.StartsCleanly(values, static (builder, _) =>
            builder.Services.Configure<TraconOptions>(static options =>
            {
                options.Skills.Scripts.EnvironmentAllowList.Clear();
                options.Skills.Scripts.EnvironmentAllowList.Add("PATH");
            }));
    }

    [Fact]
    public void An_unreadable_list_entry_is_named_by_its_position_and_the_readable_entries_stay()
    {
        var values = Keys(
            ("Tracon:Quotas:ThresholdPercents:0", "75"),
            ("Tracon:Quotas:ThresholdPercents:1", "9O"),
            ("Tracon:Quotas:ThresholdPercents:2", "95"));

        var failure = SectionBinding.FailsAtStartup(values);

        failure.Message.ShouldContain("Tracon:Quotas:ThresholdPercents:1 is '9O'");
        failure.Message.ShouldNotContain("ThresholdPercents:0");
        failure.Message.ShouldNotContain("ThresholdPercents:2");
    }

    [Fact]
    public void An_unreadable_dictionary_entry_is_named_by_its_name_and_the_other_entries_stay()
    {
        var values = Keys(
            ("Tracon:Scheduling:MaxConcurrentJobsPerLane:media", "many"),
            ("Tracon:Scheduling:MaxConcurrentJobsPerLane:default", "4"));

        var failure = SectionBinding.FailsAtStartup(values);

        failure.Message.ShouldContain("Tracon:Scheduling:MaxConcurrentJobsPerLane:media is 'many'");
        failure.Message.ShouldNotContain("MaxConcurrentJobsPerLane:default");
    }

    private static Dictionary<string, string?> Keys(params (string Key, string Text)[] entries)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (key, text) in entries)
        {
            values[key] = text;
        }

        return values;
    }

    /// <summary>Runs the startup validation and returns the message of every options failure.</summary>
    /// <remarks>
    /// Two options types that fail are two <see cref="OptionsValidationException"/>s; the host surfaces them
    /// together as one <see cref="AggregateException"/>, and a single failing type as itself.
    /// </remarks>
    private static List<string> AllStartupMessages(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddTracon(configuration.GetSection("Tracon"));

        using var provider = services.BuildServiceProvider();

        var thrown = Should.Throw<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        return thrown switch
        {
            AggregateException aggregate => [.. aggregate.InnerExceptions.Select(static inner => inner.Message)],
            _ => [thrown.Message],
        };
    }
}
