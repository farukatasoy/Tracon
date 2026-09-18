using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Wraps an <see cref="AIFunction"/> so a result over a byte limit is trimmed
/// and handed to the model inside an envelope that states how many bytes were
/// dropped, instead of running unbounded.
/// </summary>
/// <remarks>
/// <para>
/// Installed by the tool registry as the layer <strong>directly around the
/// real function</strong> — inside <c>ApprovalRequiredAIFunction</c>: trimming
/// must see only the tool's own output, never the pending-approval signal
/// that wrapper produces instead of running the body.
/// </para>
/// <para>
/// Bounding the output inside the tool's own body is always better: the tool
/// knows its data, this only counts bytes. This class is the last defence for
/// the day that bound is forgotten.
/// </para>
/// <para>
/// A result is measured by the text a provider adapter really sends
/// (<c>ToolResultText</c>), so an MCP tool — whose result is one or more
/// <see cref="AIContent"/> blocks rather than a string — is bounded on the
/// same terms as a code-defined one. A protocol result that fits is passed on
/// untouched, keeping its blocks; one that does not collapses into the same
/// envelope every other tool gets.
/// </para>
/// </remarks>
public sealed class TruncatingAIFunction : DelegatingAIFunction
{
    /// <summary>
    /// The largest an empty-content envelope can ever be — the worst case is
    /// <see cref="int.MaxValue"/> for the dropped-byte count, since that
    /// value is bounded by <see cref="Encoding.UTF8"/>'s own
    /// <see langword="int"/>-typed byte count.
    /// </summary>
    /// <remarks>
    /// A <c>maxOutputBytes</c> below this value is rejected by the
    /// constructor: below it, no input could ever be represented by a
    /// compliant envelope, and the alternative would be a silent budget
    /// violation instead of a startup-time error.
    /// </remarks>
    public static readonly int MinimumEnvelopeBytes =
        Encoding.UTF8.GetByteCount(ToolOutputEnvelope.Write(string.Empty, int.MaxValue));

    private readonly int _maxOutputBytes;

    /// <summary>Creates a new output-trimming wrapper.</summary>
    /// <param name="innerFunction">The tool to wrap.</param>
    /// <param name="maxOutputBytes">The most bytes (UTF-8) a result may carry before it is trimmed.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxOutputBytes"/> is below <see cref="MinimumEnvelopeBytes"/>.
    /// </exception>
    public TruncatingAIFunction(AIFunction innerFunction, int maxOutputBytes)
        : base(innerFunction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOutputBytes, MinimumEnvelopeBytes);

        _maxOutputBytes = maxOutputBytes;
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);

        // A raw CLR object without generated type information cannot be
        // serialized safely in an AOT-compatible library. Never call
        // Object.ToString(): it can expose data and it is not the JSON a
        // provider sends. Replace it with one stable, secret-free result.
        if (!ToolResultText.TryGetText(result, out var text))
        {
            return ToolResultText.UnsupportedResultText;
        }

        if (Encoding.UTF8.GetByteCount(text!) <= _maxOutputBytes)
        {
            // A protocol-level result that fits is handed on exactly as the
            // tool produced it: the provider adapter renders its content
            // blocks itself, and flattening an image to text to stay inside a
            // budget it never exceeded would lose the image for nothing. Every
            // other shape canonicalizes to its text here.
            return ToolResultText.IsProtocolResult(result) ? result : text;
        }

        var (envelope, omittedBytes) = ToolOutputEnvelope.Build(text!, _maxOutputBytes);

        await RecordTruncationAsync(omittedBytes).ConfigureAwait(false);

        return envelope;
    }

    /// <summary>Writes the <see cref="RunEventType.ToolOutputTruncated"/> event.</summary>
    /// <remarks>
    /// A missing run scope (a direct unit-test call, a client-side caller) is
    /// not an error — trimming still happens, only the event is skipped.
    /// <see cref="CancellationToken.None"/> is used deliberately: the trimmed
    /// result was already computed successfully, and observability must never
    /// cost that result — a cancellation racing the recording call would
    /// otherwise surface as an exception here and discard it.
    /// </remarks>
    private async ValueTask RecordTruncationAsync(int omittedBytes)
    {
        var writer = TraconRunContext.Current?.Writer;

        if (writer is null)
        {
            return;
        }

        var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{omittedBytes} byte(s) omitted (limit {_maxOutputBytes})");

        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"maxOutputBytes":{{_maxOutputBytes}},"omittedBytes":{{omittedBytes}}}""");

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.ToolOutputTruncated)
            {
                ToolName = Name,
                ToolCallId = callId,
                Text = summary,
                Payload = payload,
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    private static class ToolOutputEnvelope
    {
        private const int MaxAttempts = 8;

        private static readonly JsonWriterOptions WriterOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Builds the JSON envelope a trimmed result is wrapped in.</summary>
        /// <remarks>
        /// <para>
        /// The envelope's own size counts against <paramref name="maxBytes"/>:
        /// a bare byte-trim of the content followed by JSON-escaping it could
        /// push the finished envelope past the limit when the trimmed slice is
        /// rich in quotes or backslashes. This builds the real envelope with
        /// <see cref="Utf8JsonWriter"/> (exact escaping, no hand-written guess)
        /// and, if it still overflows, shrinks the content budget by the
        /// overflow and rebuilds — a small, bounded loop since ordinary tool
        /// output rarely needs escaping at all.
        /// </para>
        /// <para>
        /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> is used
        /// deliberately: this JSON is a tool-call result inside an API
        /// request body, never embedded in HTML, so the default encoder's
        /// conservative (and much larger) escaping of ordinary multi-byte
        /// text is not needed and would otherwise make a Turkish- or
        /// CJK-heavy result blow through the budget for no security benefit.
        /// </para>
        /// </remarks>
        public static (string Envelope, int OmittedBytes) Build(string text, int maxBytes)
        {
            var originalBytes = Encoding.UTF8.GetByteCount(text);
            var budget = maxBytes;

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var (content, _) = TextTrimming.Trim(text, Math.Max(0, budget));
                var omittedBytes = originalBytes - Encoding.UTF8.GetByteCount(content);
                var envelope = Write(content, omittedBytes);
                var envelopeBytes = Encoding.UTF8.GetByteCount(envelope);

                if (envelopeBytes <= maxBytes || budget <= 0)
                {
                    return (envelope, omittedBytes);
                }

                budget -= envelopeBytes - maxBytes;
            }

            var (empty, _) = TextTrimming.Trim(text, 0);

            return (Write(empty, originalBytes), originalBytes);
        }

        // internal, not private: TruncatingAIFunction's own MinimumEnvelopeBytes
        // field initializer calls this — a nested type's private members are
        // not visible from its enclosing type in C# (only the other direction is).
        internal static string Write(string content, int omittedBytes)
        {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("truncated", value: true);
                writer.WriteNumber("omittedBytes", omittedBytes);
                writer.WriteString("content", content);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }
}
