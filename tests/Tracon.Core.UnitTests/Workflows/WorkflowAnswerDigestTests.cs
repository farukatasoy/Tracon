using System.Security.Cryptography;
using System.Text;

namespace Tracon.Core.UnitTests.Workflows;

/// <summary>
/// The canonical form behind a workflow answer's hash: the same answer written two ways must
/// hash alike, and two different answers must not.
/// </summary>
/// <remarks>
/// A single-answer workflow refuses a different answer with 409 and lets the same one follow,
/// so a canonical form that splits one answer into two hashes would turn a retried call into a
/// conflict. The digest is pure and has no boundary to cross; a unit test is its level.
/// </remarks>
public sealed class WorkflowAnswerDigestTests
{
    [Fact]
    public void The_digest_is_64_lowercase_hexadecimal_characters()
        => WorkflowAnswerDigest.Compute("request-1", approved: true, text: null, json: null)
            .ShouldMatch("^[0-9a-f]{64}$");

    [Fact]
    public void The_digest_is_the_sha256_of_the_documented_canonical_object()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            """{"approved":true,"data":{"a":1,"b":[2,"x"]},"requestId":"request-1","text":" ok "}""")));

        WorkflowAnswerDigest.Compute("request-1", approved: true, text: " ok ", json: """{"b":[2,"x"],"a":1}""")
            .ShouldBe(expected, StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void Member_order_and_whitespace_of_the_data_do_not_change_the_digest()
    {
        var compact = WorkflowAnswerDigest.Compute("request-1", null, null, """{"decision":{"approved":true,"note":"go"},"score":3}""");
        var spread = WorkflowAnswerDigest.Compute("request-1", null, null, """
            {
              "score": 3,
              "decision": { "note": "go", "approved": true }
            }
            """);

        spread.ShouldBe(compact);
    }

    [Fact]
    public void An_escaped_string_hashes_like_the_same_string_written_plainly()
        => WorkflowAnswerDigest.Compute("request-1", null, null, """{"note":"AB"}""")
            .ShouldBe(WorkflowAnswerDigest.Compute("request-1", null, null, """{"note":"AB"}"""));

    [Fact]
    public void A_number_keeps_the_text_it_was_written_with()
        => Different(WorkflowAnswerDigest.Compute("request-1", null, null, """{"amount":1.0}"""), WorkflowAnswerDigest.Compute("request-1", null, null, """{"amount":1}"""));

    [Fact]
    public void Text_is_taken_as_written_and_never_trimmed()
        => Different(WorkflowAnswerDigest.Compute("request-1", null, "ship it", null), WorkflowAnswerDigest.Compute("request-1", null, "ship it ", null));

    [Fact]
    public void An_opposite_answer_has_a_different_digest()
        => Different(WorkflowAnswerDigest.Compute("request-1", approved: true, null, null), WorkflowAnswerDigest.Compute("request-1", approved: false, null, null));

    [Fact]
    public void A_missing_answer_differs_from_an_empty_or_negative_one()
    {
        var none = WorkflowAnswerDigest.Compute("request-1", null, null, null);

        Different(none, WorkflowAnswerDigest.Compute("request-1", null, string.Empty, null));
        Different(none, WorkflowAnswerDigest.Compute("request-1", approved: false, null, null));

        // A JSON null sent as the data is no data: both are written as "data":null.
        none.ShouldBe(WorkflowAnswerDigest.Compute("request-1", null, null, "null"));
    }

    [Fact]
    public void The_same_answer_to_another_request_has_a_different_digest()
        => Different(WorkflowAnswerDigest.Compute("request-1", approved: true, null, null), WorkflowAnswerDigest.Compute("request-2", approved: true, null, null));

    [Fact]
    public void Data_that_is_not_json_is_hashed_as_its_text_rather_than_refused()
    {
        // The HTTP body always carries JSON; a caller of the runner can pass any text, and the
        // engine refuses it later. The digest takes it as the string it is.
        WorkflowAnswerDigest.Compute("request-1", null, null, "not json")
            .ShouldBe(WorkflowAnswerDigest.Compute("request-1", null, null, "\"not json\""));
    }

    [Fact]
    public void An_approval_that_also_saves_a_rule_is_another_answer_than_one_that_does_not()
    {
        var plain = WorkflowAnswerDigest.Compute("request-1", approved: true, null, null);

        Different(plain, WorkflowAnswerDigest.Compute("request-1", approved: true, null, null, remember: true));

        // Not asking to remember is the same answer as it always was: a hash stored before the member
        // existed must still tell a repeat from a different answer.
        plain.ShouldBe(WorkflowAnswerDigest.Compute("request-1", approved: true, null, null, remember: false));
    }

    [Fact]
    public void The_digest_with_remember_is_the_sha256_of_the_documented_canonical_object()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            """{"approved":true,"data":null,"remember":true,"requestId":"request-1","text":"go ahead"}""")));

        WorkflowAnswerDigest.Compute("request-1", approved: true, text: "go ahead", json: null, remember: true)
            .ShouldBe(expected, StringCompareShould.IgnoreCase);
    }

    private static void Different(string first, string second)
        => string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse("two different answers share a digest");
}
