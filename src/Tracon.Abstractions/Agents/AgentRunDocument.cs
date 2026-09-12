namespace Tracon;

/// <summary>
/// A piece of reference text attached to a single run, kept apart from the
/// model's instructions.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a convention and an audit trail, not a security
/// guarantee.</strong> No provider gives a hard guarantee that text wrapped
/// this way is never treated as an instruction; a capable-enough model can
/// still be steered by content inside a document. The value is in keeping the
/// data channel visibly separate in the transcript and the run record, and in
/// the boundary marker surviving content that tries to imitate it.
/// </para>
/// <para>
/// A document is carried as its own <c>ChatMessage</c>, wrapped in a
/// delimiter, and marked through <c>AIContent.AdditionalProperties</c> so the
/// recording path can tell it apart from the instructions text. The run
/// record keeps only the document's name and size — never its content.
/// </para>
/// </remarks>
public sealed record AgentRunDocument
{
    /// <summary>Gets the document's name, shown to the model inside the delimiter.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the document's text. Never treated as instructions.</summary>
    public required string Content { get; init; }
}
