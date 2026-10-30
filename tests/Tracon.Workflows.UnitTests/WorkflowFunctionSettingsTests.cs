using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// A function node's settings: the function declares a settings type, the
/// node's JSON is checked by being read into it, and the function receives the
/// object that read produced.
/// </summary>
/// <remarks>
/// Everything here registers through the real <c>AddWorkflowFunction</c> extension
/// (<see cref="WorkflowTestHost.AddFunction{TInput,TOutput,TSettings}"/>), so the
/// lambda a consumer's registration builds is the one under test.
/// </remarks>
public sealed class WorkflowFunctionSettingsTests
{
    private const string Full = """{"voice":"alloy","loudnessLufs":-12.5,"speed":"Fast","maxRetries":3}""";

    [Fact]
    public async Task The_function_receives_the_object_the_node_settings_were_read_into()
    {
        var host = new WorkflowTestHost("writer");
        TtsSettings? seen = null;

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
            "tts",
            (_, settings, _, _) =>
            {
                seen = settings;

                return Say(string.Create(CultureInfo.InvariantCulture, $"{settings.Voice}@{settings.LoudnessLufs}"));
            });

        await host.SaveAsync(Chain("narrated", Node("tts", Full)));

        var events = await Collect(host.CreateRunner(), "narrated", "hello");

        seen.ShouldNotBeNull();
        seen.Voice.ShouldBe("alloy");
        seen.LoudnessLufs.ShouldBe(-12.5);
        seen.Speed.ShouldBe(SpeechSpeed.Fast);
        seen.MaxRetries.ShouldBe(3);

        // The function's output went on to the end of the chain.
        events.Single(runEvent => runEvent.Type == RunEventType.WorkflowOutput)
            .Text.ShouldNotBeNull().ShouldContain("alloy@-12.5", Case.Sensitive);
    }

    [Fact]
    public async Task A_node_without_settings_is_read_as_an_empty_object_when_nothing_is_required()
    {
        var host = new WorkflowTestHost("writer");
        OptionalSettings? seen = null;

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, OptionalSettings>(
            "mix",
            (messages, settings, _, _) =>
            {
                seen = settings;

                return new ValueTask<List<ChatMessage>>(messages);
            });

        await host.SaveAsync(Chain("mixed", Node("mix", settings: null)));
        await Collect(host.CreateRunner(), "mixed", "hello");

        seen.ShouldNotBeNull().Mode.ShouldBe("plain");
    }

    [Fact]
    public async Task One_function_serves_two_workflows_with_a_different_configuration_in_each()
    {
        // The reason the feature exists: the same code, two voices, no second registration.
        var host = new WorkflowTestHost("writer");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
            "tts",
            (_, settings, _, _) => Say($"voice={settings.Voice}"));

        await host.SaveAsync(Chain("host-a", Node("tts", """{"voice":"alloy"}""")));
        await host.SaveAsync(Chain("host-b", Node("tts", """{"voice":"nova"}""")));

        var runner = host.CreateRunner();
        var first = await Collect(runner, "host-a", "hello");
        var second = await Collect(runner, "host-b", "hello");

        Output(first).ShouldContain("voice=alloy", Case.Sensitive);
        Output(second).ShouldContain("voice=nova", Case.Sensitive);
    }

    [Theory]
    [InlineData("""{"voice":"a","voise":"b"}""", "'voise'")]
    [InlineData("""{"voice":"a","loudnessLufs":"loud"}""", "$.loudnessLufs")]
    [InlineData("""{"voice":"a","loudnessLufs":"-12"}""", "$.loudnessLufs")]
    [InlineData("""{"voice":null}""", "'voice'")]
    [InlineData("""{"voice":"a","speed":1}""", "$.speed")]
    [InlineData("""{"voice":"a","speed":"Warp"}""", "$.speed")]
    [InlineData("""{"voice":"a","maxRetries":"three"}""", "$.maxRetries")]
    [InlineData("{}", "'voice'")]
    [InlineData(null, "'voice'")]
    public void The_check_refuses_what_the_settings_type_cannot_hold_and_names_the_node_and_member(string? settings, string member)
    {
        var host = HostWithTts();

        var message = host.Functions.ValidateSettings("tts", Json(settings));

        message.ShouldNotBeNull();
        message.ShouldStartWith("Function node 'tts' has invalid settings:", Case.Sensitive);
        message.ShouldContain(member, Case.Sensitive);

        // The serializer's own line and byte position mean nothing for a parsed element.
        message.ShouldNotContain("LineNumber", Case.Sensitive);
        message.ShouldNotContain("BytePositionInLine", Case.Sensitive);
    }

    [Theory]
    [InlineData("""{"voice":"alloy"}""")]
    [InlineData("""{"Voice":"alloy"}""")]
    [InlineData("""{"voice":"alloy","maxRetries":null}""")]
    [InlineData(Full)]
    public void The_check_accepts_a_valid_object_in_any_name_case(string settings)
        => HostWithTts().Functions.ValidateSettings("tts", Json(settings)).ShouldBeNull();

    [Fact]
    public void A_refusal_never_repeats_the_value_it_refused()
    {
        // A value an operator typed may be a credential they should not have typed.
        var message = HostWithTts().Functions
            .ValidateSettings("tts", Json("""{"voice":"alloy","loudnessLufs":"sk-live-0123456789"}"""));

        message.ShouldNotBeNull();
        message.ShouldNotContain("sk-live-0123456789", Case.Sensitive);
    }

    [Fact]
    public async Task A_setter_that_rejects_a_value_by_throwing_is_a_refusal_not_a_server_error()
    {
        // The settings type is the consumer's code, and a setter can refuse a value by throwing (a range
        // check). That is the operator's input being refused: the answer says so by type name only, because the
        // exception's own message can carry the value; the exception goes to the log for the developer.
        var logs = new CollectedLogs();
        var host = new WorkflowTestHost("writer")
        {
            FunctionServices = WorkflowTestHost.ServicesFor(
                configure: services => services.AddSingleton<ILoggerFactory>(new CollectedLoggerFactory(logs))),
        };

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, ShortVoiceSettings>(
            "short",
            static (messages, _, _, _) => new ValueTask<List<ChatMessage>>(messages));

        host.Functions.ValidateSettings("short", Json("""{"voice":"abc"}""")).ShouldBeNull();

        var message = host.Functions.ValidateSettings("short", Json("""{"voice":"abcdefgh"}"""));

        message.ShouldNotBeNull();
        message.ShouldStartWith("Function node 'short' has invalid settings: the settings type rejected a value (ArgumentException)", Case.Sensitive);
        message.ShouldNotContain("too long", Case.Sensitive);
        message.ShouldNotContain("abcdefgh", Case.Sensitive);
        logs.Warnings.ShouldContain(static line => line.Contains("'short'", StringComparison.Ordinal) || line.Contains("short rejected", StringComparison.Ordinal));

        // At compile time it is the same refusal, with the consumer's exception kept as the cause.
        var exception = Should.Throw<WorkflowFunctionSettingsException>(
            () => host.Functions.CreateExecutor("short", "short", Json("""{"voice":"abcdefgh"}""")));

        exception.InnerException.ShouldBeOfType<ArgumentException>();

        // And a stored definition that carries such a value fails the run by name, without the message.
        await host.SaveAsync(Chain("narrated", Node("short", """{"voice":"abcdefgh"}""")));

        await Collect(host.CreateRunner(), "narrated", "hello");

        var root = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(static run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull().Message.ShouldContain("rejected a value (ArgumentException)", Case.Sensitive);
        root.Error.Message.ShouldNotContain("too long", Case.Sensitive);
    }

    [Fact]
    public void A_function_that_declares_no_settings_refuses_any()
    {
        var host = new WorkflowTestHost();

        host.AddFunction<string, string>("plain", static (input, _, _) => new ValueTask<string>(input));

        host.Functions.ValidateSettings("plain", null).ShouldBeNull();
        host.Functions.ValidateSettings("plain", Json("{}"))!.ShouldContain("declares none", Case.Sensitive);

        // Compile-time too: a function can lose its settings type between deployments,
        // and "ignored" would look like a setting that works.
        Should.Throw<WorkflowFunctionSettingsException>(() => host.Functions.CreateExecutor("plain", "plain", Json("{}")))
            .Message.ShouldContain("'plain'", Case.Sensitive);
    }

    [Fact]
    public void An_unregistered_function_is_reported_by_name()
        => new WorkflowTestHost().Functions.ValidateSettings("missing", null)!
            .ShouldContain("no workflow function registered with name 'missing'", Case.Sensitive);

    [Fact]
    public async Task A_definition_saved_against_an_older_settings_type_fails_the_run_before_the_first_node()
    {
        // The settings type can change between deployments. The definition below is what an
        // earlier deployment saved ("legacyField" no longer exists), written straight to the
        // store - the save endpoint would refuse it today. The run must fail NAMED, before any
        // node runs, instead of running the function with a silent default (17: the run does
        // not hang either).
        var host = HostWithTts();

        await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"alloy","legacyField":true}""")));

        var events = await Collect(host.CreateRunner(), "narrated", "hello").WaitAsync(TimeSpan.FromSeconds(30));

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull();
        root.Error.Message.ShouldContain("Workflow 'narrated' cannot run", Case.Sensitive);
        root.Error.Message.ShouldContain("Function node 'tts' has invalid settings", Case.Sensitive);
        root.Error.Message.ShouldContain("'legacyField'", Case.Sensitive);
        root.Error.Message.ShouldContain("may have changed", Case.Sensitive);

        // Nothing ran: no agent row, no executor event.
        runs.Count(run => run.Kind == RunKind.Agent).ShouldBe(0);
        events.ShouldNotContain(static runEvent => runEvent.Type == RunEventType.ExecutorInvoked);
        events.ShouldContain(static runEvent => runEvent.Type == RunEventType.RunFailed);
    }

    [Fact]
    public async Task Every_retry_attempt_sees_the_same_settings()
    {
        // The retry wrapper is built around the handler that already holds the settings, so a
        // retried node cannot see a different object on its second attempt.
        var host = new WorkflowTestHost("writer")
        {
            FunctionServices = WorkflowTestHost.ServicesFor(new TransientClassifier()),
        };
        var attempts = new List<TtsSettings>();

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
            "tts",
            (messages, settings, _, _) =>
            {
                attempts.Add(settings);

                return attempts.Count < 3
                    ? throw new TraconProviderUnavailableException("flaky-provider")
                    : new ValueTask<List<ChatMessage>>(messages);
            },
            options => options.RetryPolicy = new WorkflowNodeRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) });

        await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"nova","loudnessLufs":-20}""")));

        await Collect(host.CreateRunner(), "narrated", "hello");

        attempts.Count.ShouldBe(3);
        attempts.ShouldAllBe(settings => ReferenceEquals(settings, attempts[0]));
        attempts[0].Voice.ShouldBe("nova");
        attempts[0].LoudnessLufs.ShouldBe(-20);

        (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task Two_concurrent_runs_of_different_definition_versions_each_keep_their_own_settings()
    {
        // The graph is rebuilt for every run, so a run reads the settings once, when it starts,
        // and a save made while it is in flight is for the NEXT run.
        var host = new WorkflowTestHost("writer");
        var firstInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
            "tts",
            async (_, settings, _, cancellationToken) =>
            {
                if (string.Equals(settings.Voice, "alloy", StringComparison.Ordinal))
                {
                    firstInside.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }

                return [new ChatMessage(ChatRole.User, $"voice={settings.Voice}")];
            });

        await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"alloy"}""")));

        var runner = host.CreateRunner();
        var inFlight = Collect(runner, "narrated", "one");

        await firstInside.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The definition is saved again while the first run is inside the function.
        await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"nova"}""")));

        var second = await Collect(runner, "narrated", "two").WaitAsync(TimeSpan.FromSeconds(30));

        releaseFirst.SetResult();

        var first = await inFlight.WaitAsync(TimeSpan.FromSeconds(30));

        Output(first).ShouldContain("voice=alloy", Case.Sensitive);
        Output(second).ShouldContain("voice=nova", Case.Sensitive);
    }

    [Fact]
    public async Task A_resume_runs_with_the_settings_of_the_version_the_run_recorded()
    {
        // An edit made while a run waited must not change what resumes. The run recorded
        // version 1; the operator then saved voice=nova as version 2; the resume still speaks alloy.
        var host = new WorkflowTestHost("writer");
        var voices = new ConcurrentQueue<string>();

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
            "tts",
            (messages, settings, _, _) =>
            {
                voices.Enqueue(settings.Voice);

                return new ValueTask<List<ChatMessage>>(messages);
            });

        await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"alloy"}""")));

        var runner = host.CreateRunner();
        await Collect(runner, "narrated", "hello");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);
        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, first.Id);

        checkpoints.Count.ShouldBeGreaterThanOrEqualTo(2);
        first.AgentVersion.ShouldBe(1, "the run row names the version it was compiled from");

        await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"nova"}""")));

        // Resume from the checkpoint written BEFORE the function ran: it runs again.
        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.ResumeStreamingAsync(new WorkflowResumeRequest
        {
            RunId = first.Id,
            CheckpointId = checkpoints[0].CheckpointId,
        }))
        {
            resumed.Add(runEvent);
        }

        voices.ToArray().ShouldBe(["alloy", "alloy"]);
        resumed.ShouldNotContain(static runEvent => runEvent.Type == RunEventType.RunFailed);

        var resumeRow = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow && run.Id != first.Id);

        resumeRow.AgentVersion.ShouldBe(1, "the resume is stamped with the version it ran, not the current one");
    }

    [Fact]
    public async Task A_resume_that_names_a_newer_version_reads_its_settings_and_the_old_checkpoint_stays_compatible()
    {
        // The executor id is the node's name; the settings are not part of it. So a settings change
        // never makes a checkpoint incompatible, and an operator who names the newer version on
        // purpose carries the waiting run over the fix.
        var host = new WorkflowTestHost("writer");
        var voices = new ConcurrentQueue<string>();

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
            "tts",
            (messages, settings, _, _) =>
            {
                voices.Enqueue(settings.Voice);

                return new ValueTask<List<ChatMessage>>(messages);
            });

        await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"alloy"}""")));

        var runner = host.CreateRunner();
        await Collect(runner, "narrated", "hello");

        var first = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);
        var checkpoints = await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, first.Id);

        var edited = await host.SaveAsync(Chain("narrated", Node("tts", """{"voice":"nova"}""")));

        var resumed = new List<RunEvent>();

        await foreach (var runEvent in runner.ResumeStreamingAsync(new WorkflowResumeRequest
        {
            RunId = first.Id,
            CheckpointId = checkpoints[0].CheckpointId,
            DefinitionVersion = edited.Version,
        }))
        {
            resumed.Add(runEvent);
        }

        voices.ToArray().ShouldBe(["alloy", "nova"]);
        resumed.ShouldNotContain(static runEvent => runEvent.Type == RunEventType.RunFailed);
    }

    [Fact]
    public void The_exported_schema_lists_the_members_the_check_accepts()
    {
        var descriptor = HostWithTts().Functions.List().Single(static candidate => string.Equals(candidate.Name, "tts", StringComparison.Ordinal));

        descriptor.SettingsType.ShouldBe(typeof(TtsSettings));

        var schema = descriptor.SettingsSchema.ShouldNotBeNull();
        var properties = schema.GetProperty("properties");

        properties.GetProperty("voice").GetProperty("type").GetString().ShouldBe("string");
        properties.GetProperty("loudnessLufs").GetProperty("type").GetString().ShouldBe("number");

        // Names the enum by name: the check refuses an integer.
        properties.GetProperty("speed").GetProperty("enum").EnumerateArray()
            .Select(static value => value.GetString()).ShouldBe(["Slow", "Normal", "Fast"]);

        schema.GetProperty("required").EnumerateArray().Select(static value => value.GetString()).ShouldBe(["voice"]);

        // A misspelled name is refused, not ignored - the schema says the same.
        schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void A_function_without_settings_has_neither_a_type_nor_a_schema()
    {
        var host = new WorkflowTestHost();

        host.AddFunction<string, string>("plain", static (input, _, _) => new ValueTask<string>(input));

        var descriptor = host.Functions.List().Single();

        descriptor.SettingsType.ShouldBeNull();
        descriptor.SettingsSchema.ShouldBeNull();
    }

    [Fact]
    public void A_settings_type_with_a_member_the_exporter_cannot_describe_still_registers_and_exports()
    {
        var host = new WorkflowTestHost();

        host.AddFunction<string, string, UnsupportedMemberSettings>("odd", static (input, _, _, _) => new ValueTask<string>(input));

        var descriptor = host.Functions.List().Single();

        descriptor.SettingsSchema.ShouldNotBeNull();
        host.Functions.ValidateSettings("odd", Json("""{"name":"x"}""")).ShouldBeNull();
    }

    [Fact]
    public void A_schema_the_exporter_cannot_produce_costs_the_console_its_field_list_never_the_function()
    {
        // An open generic is a type the exporter refuses (and no settings could be read into
        // it either) - the registration is built by hand because AddWorkflowFunction would
        // refuse it first. The point is the registry: it must keep the function and say why.
        var logs = new CollectedLogs();
        var host = new WorkflowTestHost
        {
            FunctionServices = WorkflowTestHost.ServicesFor(configure: services => services.AddSingleton<ILoggerFactory>(new CollectedLoggerFactory(logs))),
        };

        host.RegisterRaw(new WorkflowFunctionRegistration(
            new WorkflowFunctionDescriptor
            {
                Name = "odd",
                InputType = typeof(string),
                OutputType = typeof(string),
                SettingsType = typeof(List<>),
            },
            static _ => static (executorId, _, _) => new FunctionExecutor<string, string>(
                executorId,
                static (input, _, _) => new ValueTask<string>(input))));

        var registry = host.Functions;

        registry.Contains("odd").ShouldBeTrue();

        // A bare object schema, NOT null: null tells the console "this function takes no settings".
        var schema = registry.List().Single().SettingsSchema.ShouldNotBeNull();

        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.TryGetProperty("properties", out _).ShouldBeFalse();
        logs.Warnings.ShouldContain(static line => line.Contains("odd", StringComparison.Ordinal));
    }

    [Fact]
    public void A_settings_type_no_settings_could_be_read_into_fails_at_registration()
    {
        var host = new WorkflowTestHost();

        var exception = Should.Throw<TraconException>(() =>
            host.AddFunction<string, string, AbstractSettings>("abstract", static (input, _, _, _) => new ValueTask<string>(input)));

        exception.Message.ShouldContain("'abstract'", Case.Sensitive);
        exception.Message.ShouldContain("cannot be read", Case.Sensitive);
    }

    [Fact]
    public async Task Settings_over_the_limit_are_refused_when_saving_but_a_stored_definition_still_runs()
    {
        var host = new WorkflowTestHost("writer")
        {
            FunctionServices = WorkflowTestHost.ServicesFor(
                configure: services => services.AddOptions<TraconWorkflowOptions>().Configure(static options => options.MaxNodeSettingsLength = 48)),
        };

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>("tts", static (_, settings, _, _) => Say(settings.Voice));

        var small = """{"voice":"alloy"}""";
        var large = $$"""{"voice":"{{new string('v', 100)}}"}""";

        host.Functions.ValidateSettings("tts", Json(small)).ShouldBeNull();

        var message = host.Functions.ValidateSettings("tts", Json(large));

        message.ShouldNotBeNull();
        message.ShouldContain("larger than 48 bytes", Case.Sensitive);
        message.ShouldContain(nameof(TraconWorkflowOptions.MaxNodeSettingsLength), Case.Sensitive);

        // The cap guards a SAVE. A definition stored before the cap was lowered keeps running.
        await host.SaveAsync(Chain("narrated", Node("tts", large)));

        var events = await Collect(host.CreateRunner(), "narrated", "hello");

        Output(events).ShouldContain(new string('v', 100), Case.Sensitive);
    }

    [Fact]
    public void A_node_cannot_hold_an_unset_or_null_element_as_its_settings()
    {
        // An unset JsonElement cannot be written as JSON: one such node would fail the whole workflow list.
        new WorkflowNodeReference { Name = "tts", Kind = WorkflowNodeKind.Function, Settings = default(JsonElement) }
            .Settings.ShouldBeNull();
        new WorkflowNodeReference { Name = "tts", Kind = WorkflowNodeKind.Function, Settings = Json("null") }
            .Settings.ShouldBeNull();
        new WorkflowNodeReference { Name = "tts", Kind = WorkflowNodeKind.Function, Settings = Json("{}") }
            .Settings.ShouldNotBeNull();
    }

    [Fact]
    public void The_serialized_node_round_trips_its_settings()
    {
        var node = Node("tts", Full);
        var json = JsonSerializer.Serialize(node, JsonSerializerOptions.Web);
        var back = JsonSerializer.Deserialize<WorkflowNodeReference>(json, JsonSerializerOptions.Web)!;

        back.Settings.ShouldNotBeNull().GetProperty("voice").GetString().ShouldBe("alloy");
        back.Settings.Value.GetProperty("loudnessLufs").GetDouble().ShouldBe(-12.5);

        // No settings: written as null, read back as null (not as an unset element).
        var plain = JsonSerializer.Serialize(Node("tts", null), JsonSerializerOptions.Web);

        plain.ShouldContain("\"settings\":null", Case.Sensitive);
        JsonSerializer.Deserialize<WorkflowNodeReference>(plain, JsonSerializerOptions.Web)!.Settings.ShouldBeNull();
    }

    private static WorkflowTestHost HostWithTts()
    {
        var host = new WorkflowTestHost("writer");

        host.AddFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>("tts", static (messages, _, _, _) => new ValueTask<List<ChatMessage>>(messages));

        return host;
    }

    private static WorkflowDefinition Chain(string name, WorkflowNodeReference function)
        => new()
        {
            Name = name,
            Kind = WorkflowKind.Sequential,
            Nodes = [new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent }, function],
        };

    private static WorkflowNodeReference Node(string name, string? settings)
        => new() { Name = name, Kind = WorkflowNodeKind.Function, Settings = Json(settings) };

    private static JsonElement? Json(string? json)
        => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    private static ValueTask<List<ChatMessage>> Say(string text)
        => new([new ChatMessage(ChatRole.User, text)]);

    private static string Output(IEnumerable<RunEvent> events)
        => events.Single(static runEvent => runEvent.Type == RunEventType.WorkflowOutput).Text.ShouldNotBeNull();

    private static async Task<List<RunEvent>> Collect(WorkflowRunner runner, string name, string message)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = name, Message = message }))
        {
            events.Add(runEvent);
        }

        return events;
    }

    internal enum SpeechSpeed
    {
        Slow,
        Normal,
        Fast,
    }

    internal sealed record TtsSettings
    {
        public required string Voice { get; init; }

        public double LoudnessLufs { get; init; } = -16;

        public SpeechSpeed Speed { get; init; } = SpeechSpeed.Normal;

        public int? MaxRetries { get; init; }
    }

    internal sealed record OptionalSettings
    {
        public string Mode { get; init; } = "plain";
    }

    internal sealed class ShortVoiceSettings
    {
        public required string Voice
        {
            get;
            init => field = value.Length <= 5 ? value : throw new ArgumentException($"Voice '{value}' is too long", nameof(value));
        }
    }

    internal sealed class UnsupportedMemberSettings
    {
        public string Name { get; set; } = string.Empty;

        public Type? Kind { get; set; }
    }

    internal abstract class AbstractSettings
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TransientClassifier : IRunErrorClassifier
    {
        public RunErrorClassification Classify(RunError runError)
            => new() { Class = RunErrorClass.ProviderUnavailable, Fingerprint = "transient" };
    }

    private sealed class CollectedLogs
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IEnumerable<string> Warnings => _warnings;

        public void Add(string line) => _warnings.Enqueue(line);
    }

    private sealed class CollectedLoggerFactory(CollectedLogs logs) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CollectedLogger(logs);

        public void Dispose()
        {
        }

        private sealed class CollectedLogger(CollectedLogs logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    logs.Add(formatter(state, exception));
                }
            }
        }
    }
}
