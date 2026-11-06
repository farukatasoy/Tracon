using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tool that fails during a run: the key the provider is sent appears nowhere, and the text of the failure reaches only
/// the outputs its kind is allowed to reach.
/// </summary>
/// <remarks>
/// <para>
/// The provider asks for the tool, the tool throws, and the model's next turn is accepted, refused (quoting the key it was
/// sent) or held open while the client leaves. The failure comes in two kinds. A <see cref="TraconException"/> is a
/// sentence Tracon wrote for the model, so its text is the tool's result and the model is told it. Any other exception
/// carries whatever its author put in it (a connection string, a path): the model is told a fixed sentence, the run
/// record keeps a short safe text, and the full text goes only to the log of the host's operator.
/// </para>
/// <para>
/// The run reaches the provider twice for each path (the tool call, then the turn after the tool's result), the second
/// request body is kept, and the credential the provider was sent is checked: a search that finds nothing proves nothing
/// unless the run got that far.
/// </para>
/// </remarks>
public sealed class ToolFailureCanaryTests
{
    [Theory]
    [InlineData(CanaryKeyKind.Setup, true, true)]
    [InlineData(CanaryKeyKind.Setup, true, false)]
    [InlineData(CanaryKeyKind.Setup, false, true)]
    [InlineData(CanaryKeyKind.Setup, false, false)]
    [InlineData(CanaryKeyKind.Tenant, true, true)]
    [InlineData(CanaryKeyKind.Tenant, true, false)]
    [InlineData(CanaryKeyKind.Tenant, false, true)]
    [InlineData(CanaryKeyKind.Tenant, false, false)]
    public async Task A_failing_tool_leaves_the_key_in_no_output_and_its_text_only_where_its_kind_belongs(CanaryKeyKind kind, bool explained, bool recordSensitiveData)
    {
        var scenario = explained ? "toolfail-explained" : "toolfail-plain";
        var toolText = $"canary-{scenario}-text";
        var calls = 0;

        using var database = new TempSqliteDatabase("tool-failure-canary");

        await using var canary = await KeyedCanaryHost.StartAsync(
            kind,
            scenario,
            database,
            configure: builder => builder.AddTool(
                (Func<string>)(() =>
                {
                    Interlocked.Increment(ref calls);

                    throw explained
                        ? new TraconException($"The order system refused the call: {toolText}-0e5a1b.")
                        : new InvalidOperationException($"Server=db.internal;Password={toolText}-0e5a1b"); // SYNTHETIC-CREDENTIAL: the text a tool's exception carries, searched for in every output
                }),
                name: KeyedCanaryHost.ToolName,
                description: "Looks an order up."),
            recordSensitiveData: recordSensitiveData);

        var drive = canary.Drive;
        var reader = canary.NewDrive();
        var wire = canary.Wire;

        wire.ToolCall = KeyedCanaryHost.ToolName;

        // The provider asks for the tool and accepts the turn after it, then asks again and refuses (quoting the key), then
        // asks and holds the next turn open while the client leaves.
        wire.Mode = CanaryWireServer.Behavior.Accept;
        await drive.RunTheSixWaysAsync(KeyedCanaryHost.AgentName);

        wire.Mode = CanaryWireServer.Behavior.RefuseAndQuote;
        await drive.RunTheSixWaysAsync(KeyedCanaryHost.AgentName);

        wire.Mode = CanaryWireServer.Behavior.Hold;
        await drive.AbandonStreamedRunAsync(KeyedCanaryHost.AgentName, TimeSpan.FromMilliseconds(600));
        await drive.WaitForRunsToSettleAsync();

        wire.Mode = CanaryWireServer.Behavior.Accept;

        var answered = await reader.ReadEverythingAsync();
        var bytes = database.ReadAllBytes();

        // Proof that the run got as far as the search needs.
        answered.ShouldBeGreaterThan(40, "the read sweep reached too few operations for an empty search to mean anything");
        calls.ShouldBeGreaterThanOrEqualTo(13, "the tool was not called on every path");
        wire.Credentials.ShouldContain(credential => credential.Contains(KeyedCanaryHost.KeyOf(kind, scenario), StringComparison.Ordinal), "the provider was never sent the key");

        var afterTool = string.Join('\n', wire.Bodies.Where(static body => body.Contains("\"role\":\"tool\"", StringComparison.Ordinal)));

        wire.Bodies.Count(static body => body.Contains("\"role\":\"tool\"", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(13, "the turn after the tool's result never reached the provider on most paths");
        SecretHits.FindInBytes(bytes, KeyedCanaryHost.AgentName).ShouldNotBeEmpty("the runs are not in the database, so the scan reads the wrong place");

        var outputs = canary.Outputs(
            [
                new("the request the model received after the tool's result", afterTool),
                new("run reads (record, events, tools, export, audit)", reader.Seen),
            ]);

        outputs["responses and records"] = drive.Seen;

        // The provider's key is nowhere.
        var keyHits = SecretHits.Find(outputs, [new("provider key", canary.Stem)]);

        foreach (var encoding in SecretHits.FindInBytes(bytes, canary.Stem))
        {
            keyHits.Add($"the provider key is in the database as {encoding}");
        }

        keyHits.ShouldBeEmpty();

        // The failure's text. An explained failure is a sentence written for the model, so the model is told it. Any other
        // failure's text goes to the operator's log and nowhere the model, a caller of the run record or a metric can read it.
        var where = SecretHits.Find(outputs, [new("tool failure text", toolText)])
            .Select(static hit => hit[..hit.IndexOf(": ...", StringComparison.Ordinal)])
            .ToList();

        bool In(string output) => where.Any(hit => hit.EndsWith("is in the " + output, StringComparison.Ordinal));

        In("log lines").ShouldBeTrue("the failure was never logged, so the tool did not fail the way this test is about");
        In("metric tags").ShouldBeFalse();

        if (explained)
        {
            In("the request the model received after the tool's result").ShouldBeTrue("the model was not told the sentence written for it");
        }
        else
        {
            In("the request the model received after the tool's result").ShouldBeFalse("the model was told the text of an exception that is not Tracon's own");

            // With the traces' sensitive recording off (the default), the span's failure message is not stored, so no read of the
            // run and no byte of the database holds it. With it on, the trace of the run keeps the message, as it keeps every
            // prompt and result; that is the flag's purpose and is not asserted either way.
            if (!recordSensitiveData)
            {
                In("run reads (record, events, tools, export, audit)").ShouldBeFalse("a read of the run record shows the text of an exception that is not Tracon's own");
                SecretHits.FindInBytes(bytes, toolText).ShouldBeEmpty("the text of an exception that is not Tracon's own is in the database");
            }
        }
    }
}
