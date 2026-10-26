using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>Builds the tool chain of the tools Tracon adds to a run itself, over fakes.</summary>
internal static class BuiltInToolGovernanceTestSupport
{
    /// <summary>A governance that allows every call, or asks <paramref name="handler"/> when one is given.</summary>
    /// <param name="handler">The authorization handler; <see langword="null"/> allows every call.</param>
    /// <param name="validator">The argument validator; <see langword="null"/> accepts every call.</param>
    public static BuiltInToolGovernance Create(IToolAuthorizationHandler? handler = null, IToolArgumentsValidator? validator = null)
        => new(
            handler ?? new AllowAllToolAuthorizationHandler(),
            validator ?? NoOpToolArgumentsValidator.Instance,
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions()),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);
}

/// <summary>An authorization handler that denies every call and remembers what it was asked.</summary>
internal sealed class DenyingToolHandler(string reason) : IToolAuthorizationHandler
{
    private readonly List<ToolAuthorizationRequest> _seen = [];

    /// <summary>Gets the requests the handler received, in order.</summary>
    public IReadOnlyList<ToolAuthorizationRequest> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<ToolAuthorizationResult> AuthorizeAsync(ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        lock (_seen)
        {
            _seen.Add(request);
        }

        return new(ToolAuthorizationResult.Deny(reason));
    }
}
