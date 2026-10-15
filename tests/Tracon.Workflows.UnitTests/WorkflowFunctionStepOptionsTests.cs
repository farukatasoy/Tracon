using System.Text.Json;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="WorkflowFunctionStepOptions.SetSettings{TSettings}"/> (phase 211): a typed value becomes the JSON element a
/// stored node carries, written with the options the strict read uses, so the same check applies to both.
/// </summary>
public sealed class WorkflowFunctionStepOptionsTests
{
    [Fact]
    public void A_typed_value_is_written_as_the_json_object_the_strict_read_takes_back()
    {
        var options = new WorkflowFunctionStepOptions();

        options.SetSettings(new VoiceSettings { Voice = "mira", Speed = 1.25, Mode = Mode.Fast });

        var settings = options.Settings.ShouldNotBeNull();

        settings.ValueKind.ShouldBe(JsonValueKind.Object);
        settings.GetProperty("voice").GetString().ShouldBe("mira");

        // Names are camelCase and an enum is written by name: what the strict read accepts.
        settings.GetProperty("mode").GetString().ShouldBe("Fast");

        var read = WorkflowFunctionSettings.Deserialize<VoiceSettings>("tts", settings);

        read.Voice.ShouldBe("mira");
        read.Speed.ShouldBe(1.25);
        read.Mode.ShouldBe(Mode.Fast);
    }

    [Fact]
    public void A_null_value_is_refused()
    {
        var options = new WorkflowFunctionStepOptions();

        Should.Throw<ArgumentNullException>(() => options.SetSettings<VoiceSettings>(null!));
        options.Settings.ShouldBeNull();
    }

    [Fact]
    public void The_settings_default_to_none_and_a_timeout_to_the_registered_one()
    {
        var options = new WorkflowFunctionStepOptions();

        options.Settings.ShouldBeNull();
        options.Timeout.ShouldBeNull();
    }

    private enum Mode
    {
        Slow,
        Fast,
    }

    private sealed record VoiceSettings
    {
        public required string Voice { get; init; }

        public double Speed { get; init; } = 1;

        public Mode Mode { get; init; }
    }
}
