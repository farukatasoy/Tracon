using Microsoft.Extensions.Hosting;

namespace Tracon;

/// <summary>What the host around the voice conversation connections says and decides.</summary>
/// <remarks>
/// <para>
/// A connection asks it whether the host is stopping, so that a conversation the host ends is closed and recorded with the
/// reason that says so, and whether a run may start, so that a conversation answers to the same rules as every other way to
/// start a run. It is one collaborator, and not one parameter for each question, so that the driver's constructor does not
/// grow with every question a connection learns to ask of the host.
/// </para>
/// </remarks>
internal sealed class VoiceHostContext
{
    private readonly IHostApplicationLifetime? _lifetime;
    private readonly RunAdmission _admission;

    /// <summary>Initializes a new instance of the <see cref="VoiceHostContext"/> class.</summary>
    /// <param name="lifetime">
    /// The host's lifetime; <see langword="null"/> when the host has none (a bare service collection), in which case a
    /// connection the host cuts is recorded as the client's.
    /// </param>
    /// <param name="admission">Asks the questions the HTTP endpoints ask before a run starts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="admission"/> is <see langword="null"/>.</exception>
    public VoiceHostContext(IHostApplicationLifetime? lifetime, RunAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);

        _lifetime = lifetime;
        _admission = admission;
    }

    /// <summary>
    /// Asks whether the caller of the current connection may start a run of an agent in a session: the registered run
    /// authorization handler, then the session's owner, then the tenant's quota.
    /// </summary>
    /// <param name="agentName">The agent the run would start.</param>
    /// <param name="sessionId">The session the run would continue or open.</param>
    /// <param name="cancellationToken">The cancellation token of the connection or of the turn.</param>
    /// <returns>The decision; a refusal is a value, as it is for the HTTP endpoints.</returns>
    public ValueTask<RunAdmissionDecision> CheckRunAsync(string agentName, string sessionId, CancellationToken cancellationToken)
        => _admission.CheckAsync(RunChannel.Voice, agentName, sessionId, cancellationToken);

    /// <summary>Registers a callback that runs when the host begins to stop.</summary>
    /// <param name="callback">The callback; it runs once, on the thread that stops the host.</param>
    /// <returns>The registration; disposing it removes the callback.</returns>
    public CancellationTokenRegistration OnStopping(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return _lifetime?.ApplicationStopping.Register(callback) ?? default;
    }
}
