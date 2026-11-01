using System.Runtime.CompilerServices;

namespace Tracon.Tests.Common;

/// <summary>
/// Proves the guard is installed in the assembly it is linked into.
/// </summary>
public sealed class UnobservedTaskGuardTests
{
    [Fact]
    public void The_guard_sees_a_faulted_task_that_nobody_observed()
    {
        var marker = $"canary-{Guid.NewGuid():N}";

        DropFaultedTask(marker);

        UnobservedTaskGuard.ForceCollection();

        UnobservedTaskGuard
            .Take(exception => exception.Message.Contains(marker, StringComparison.Ordinal))
            .ShouldHaveSingleItem("The guard was not subscribed, so an unobserved fault in this assembly would pass unnoticed.");
    }

    /// <summary>A separate method so no local keeps the task alive until the collection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DropFaultedTask(string marker)
    {
        var source = new TaskCompletionSource();
        source.SetException(new InvalidOperationException(marker));
    }
}
