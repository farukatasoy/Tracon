using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Stores the audio a conversation spoke as an attachment, when the host asked for it to be kept.</summary>
/// <remarks>
/// Only the <strong>audio that the agent produced</strong> is stored. The audio of the user is never written: voice is
/// biometric data, and the record of what was said is already the transcript in the session history. A failure to store is
/// logged and the conversation goes on.
/// </remarks>
internal sealed class VoiceAudioArchive
{
    private readonly IAttachmentStore _attachments;
    private readonly AttachmentTypeGuard _guard;
    private readonly ILogger<VoiceAudioArchive> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="VoiceAudioArchive"/> class.</summary>
    /// <param name="attachments">The attachment store.</param>
    /// <param name="guard">The attachment type guard.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeProvider">The time source; the system's when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public VoiceAudioArchive(
        IAttachmentStore attachments,
        AttachmentTypeGuard guard,
        ILogger<VoiceAudioArchive> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(logger);

        _attachments = attachments;
        _guard = guard;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Stores the spoken response of one turn.</summary>
    /// <param name="data">The audio the agent produced.</param>
    /// <param name="request">The connection the audio belongs to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The attachment identifier, or <see langword="null"/> when nothing was stored.</returns>
    public async Task<string?> SaveAsync(byte[] data, VoiceConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(request);

        if (data.Length == 0)
        {
            return null;
        }

        // The attachment store DOES NOT validate; the guard is called explicitly here.
        var validation = _guard.Validate(data);

        if (!validation.IsValid)
        {
            _logger.LogWarning("The voice audio could not be stored as an attachment: {Error}", validation.Error);
            return null;
        }

        try
        {
            var descriptor = await _attachments.SaveAsync(
                new AttachmentContent
                {
                    TenantId = request.TenantId,

                    // The session identifier is REQUIRED: when it is left empty the retention policy treats the attachment as
                    // orphaned and deletes it.
                    SessionId = request.SessionId,
                    FileName = AttachmentFileNames.For("voice", validation.MediaType!, _timeProvider.GetUtcNow()),
                    MediaType = validation.MediaType!,
                    Data = data,
                    CreatedBy = request.CreatedBy,
                },
                cancellationToken).ConfigureAwait(false);

            return descriptor.Id.ToString("D");
        }
        catch (Exception exception) when (exception is TraconException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "The voice audio could not be stored as an attachment.");
            return null;
        }
    }
}
