using System.Text.Json;
using Microsoft.Extensions.AI.Evaluation;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>
/// The shape <c>EvalCaseResult.Scores</c> carries (phase 152).
/// </summary>
/// <remarks>
/// A pure function over a metric dictionary: it crosses no boundary, so a unit
/// test proves it. Before phase 152 it wrote only name/passed/reason, and every
/// non-boolean metric lost its value on the way to the jsonb column.
/// </remarks>
public sealed class SerializeScoresTests
{
    [Fact]
    public void A_numeric_metrics_value_survives()
    {
        var element = Serialize(new NumericMetric("similarity", 0.87)
        {
            Interpretation = new EvaluationMetricInterpretation(EvaluationRating.Good, failed: false, reason: null),
        });

        element[0].GetProperty("kind").GetString().ShouldBe("numeric");
        element[0].GetProperty("value").GetDouble().ShouldBe(0.87);
        element[0].GetProperty("passed").GetBoolean().ShouldBeTrue();
        element[0].GetProperty("rating").GetString().ShouldBe("Good");
    }

    [Fact]
    public void A_boolean_metrics_value_survives()
    {
        var element = Serialize(new BooleanMetric("grounded", value: false));

        element[0].GetProperty("kind").GetString().ShouldBe("boolean");
        element[0].GetProperty("value").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void A_string_metrics_value_survives()
    {
        var element = Serialize(new StringMetric("severity", "minor"));

        element[0].GetProperty("kind").GetString().ShouldBe("string");
        element[0].GetProperty("value").GetString().ShouldBe("minor");
    }

    [Fact]
    public void A_metric_with_no_measurement_writes_null_not_zero()
    {
        var element = Serialize(new NumericMetric("similarity", value: null));

        element[0].GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void An_uninterpreted_metric_carries_a_null_passed_and_no_rating()
    {
        var element = Serialize(new BooleanMetric("grounded", value: true));

        element[0].GetProperty("passed").ValueKind.ShouldBe(JsonValueKind.Null);
        element[0].TryGetProperty("rating", out _).ShouldBeFalse();
    }

    [Fact]
    public void Diagnostics_are_written_with_their_severity()
    {
        var metric = new NumericMetric("similarity", 0.4)
        {
            Diagnostics = [EvaluationDiagnostic.Warning("the context was truncated")],
        };

        var element = Serialize(metric);
        var diagnostics = element[0].GetProperty("diagnostics");

        diagnostics.GetArrayLength().ShouldBe(1);
        diagnostics[0].GetProperty("severity").GetString().ShouldBe("Warning");
        diagnostics[0].GetProperty("message").GetString().ShouldBe("the context was truncated");
    }

    [Fact]
    public void A_metric_with_no_diagnostics_writes_no_diagnostics_array()
        => Serialize(new BooleanMetric("grounded", value: true))[0]
            .TryGetProperty("diagnostics", out _).ShouldBeFalse();

    [Fact]
    public void Metadata_is_NOT_written()
    {
        // A scope boundary, not an omission: AddOrUpdateChatMetadata can put
        // model response metadata here and EvalCaseResult.Scores is served to
        // clients.
        var metric = new BooleanMetric("grounded", value: true)
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["model"] = "gpt-4.1" },
        };

        var element = Serialize(metric);

        element[0].TryGetProperty("metadata", out _).ShouldBeFalse();
        element.GetRawText().ShouldNotContain("gpt-4.1");
    }

    private static JsonElement Serialize(params EvaluationMetric[] metrics)
        => EvalJobHandler.SerializeScores(
            metrics.ToDictionary(static metric => metric.Name, StringComparer.Ordinal));
}
