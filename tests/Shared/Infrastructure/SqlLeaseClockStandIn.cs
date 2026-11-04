using System.Reflection;

namespace Tracon.Tests.Common;

/// <summary>
/// Puts a clock a test can move in the place of the database server's clock in the lease statements of a SQL query set.
/// </summary>
/// <remarks>
/// <para>
/// The PostgreSQL and SQL Server lease statements read the server's own clock inside the statement, and a test cannot move
/// that clock. The differential job sequences need to: a lease must expire, a retry delay must run out and a job must come
/// due when a step says so. This replaces the server's clock expression in the six lease statements with a function the
/// test's schema holds (it returns a time a row stores), and changes nothing else in them.
/// </para>
/// <para>
/// It fails when a statement does not contain the server's expression, so a lease statement that is rewritten cannot keep
/// being driven by a stand-in that no longer matches it. The server's own function is covered by the scenarios that run
/// on real time: the store contracts and the clock-skew contract.
/// </para>
/// </remarks>
internal static class SqlLeaseClockStandIn
{
    private static readonly string[] LeaseStatements =
    [
        nameof(SqlQueriesBase.LeaseJob),
        nameof(SqlQueriesBase.LeaseJobWithTenantCap),
        nameof(SqlQueriesBase.RenewJobLease),
        nameof(SqlQueriesBase.ReleaseJobForRetry),
        nameof(SqlQueriesBase.AcquireSingletonLease),
        nameof(SqlQueriesBase.RenewSingletonLease),
    ];

    /// <summary>Replaces the server's clock expression in the lease statements; a statement already replaced is left alone.</summary>
    /// <param name="queries">The query set of a store context; its texts are changed in place.</param>
    /// <param name="serverClock">The expression the provider's lease statements read the time with.</param>
    /// <param name="standIn">The expression to read it with instead.</param>
    public static void Install(SqlQueriesBase queries, string serverClock, string standIn)
    {
        ArgumentNullException.ThrowIfNull(queries);

        foreach (var name in LeaseStatements)
        {
            var property = typeof(SqlQueriesBase).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
            var text = (string)property.GetValue(queries)!;

            if (text.Contains(standIn, StringComparison.Ordinal))
            {
                continue;
            }

            text.ShouldContain(serverClock, customMessage: $"{name} no longer reads the server's clock as '{serverClock}'");
            property.GetSetMethod(nonPublic: true)!.Invoke(queries, [text.Replace(serverClock, standIn, StringComparison.Ordinal)]);
        }
    }
}
