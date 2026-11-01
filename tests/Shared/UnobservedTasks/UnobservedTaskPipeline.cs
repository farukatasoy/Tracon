using Tracon.Tests.Common;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestPipelineStartup(typeof(UnobservedTaskPipeline))]

namespace Tracon.Tests.Common;

/// <summary>
/// Settles the guard when the test run ends, while the runtime can still finalize.
/// </summary>
/// <remarks>
/// xunit allows ONE pipeline startup type per assembly. A test project that needs its
/// own folds this check into it by calling <c>UnobservedTaskGuard.SettleAtEndOfRun</c>
/// from its <c>StopAsync</c>.
/// </remarks>
public sealed class UnobservedTaskPipeline : ITestPipelineStartup
{
    /// <inheritdoc />
    public ValueTask StartAsync(IMessageSink diagnosticMessageSink) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask StopAsync()
    {
        UnobservedTaskGuard.SettleAtEndOfRun();

        return ValueTask.CompletedTask;
    }
}
