using System.Data.Common;
using System.Net.Sockets;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// What counts as "the persistence store could not answer": the one question the agent catalog and the host's
/// <c>503</c> mapping both ask.
/// </summary>
/// <remarks>
/// The catalog wraps the failure of a source as "the agent could not be compiled" (a <c>400</c>) and the host turns a store
/// failure into a <c>503</c>. Each asked for itself and the two disagreed: the catalog let only a provider's
/// <see cref="DbException"/> through, so a pool that ran dry or a connect that timed out was still a <c>400</c> that sent the
/// caller to fix an agent that was fine.
/// </remarks>
public sealed class StoreFailureTests
{
    /// <summary>
    /// The text SqlClient puts on the <see cref="InvalidOperationException"/> it throws when a connection is not free within
    /// the connect timeout (<c>ADP_PooledOpenTimeout</c>, read from the driver's resources).
    /// </summary>
    private const string SqlClientPoolTimeout =
        "Timeout expired.  The timeout period elapsed prior to obtaining a connection from the pool.  " +
        "This may have occurred because all pooled connections were in use and max pool size was reached.";

    [Fact]
    public void A_provider_exception_is_a_store_failure()
        => StoreFailure.Is(new ProviderException()).ShouldBeTrue();

    [Fact]
    public void A_value_the_store_refused_is_still_a_store_failure()
    {
        // The two questions are different: this one asks whether the failure came from the store at all, and a refused text
        // did. The host asks the provider's classifier whether the store refused the value; an answer of "no" here would
        // send the catalog, the decorator chain and the start-up guard down the wrong branch.
        StoreFailure.Is(new ProviderException("String or binary data would be truncated.")).ShouldBeTrue();
    }

    [Fact]
    public void A_connection_pool_that_ran_dry_is_a_store_failure()
        => StoreFailure.Is(new InvalidOperationException(SqlClientPoolTimeout)).ShouldBeTrue();

    [Theory]
    [InlineData("The connection pool has been exhausted, either raise 'Maximum Pool Size' (currently 10) or 'Timeout'.")]
    [InlineData("THE CONNECTION POOL HAS BEEN EXHAUSTED")]
    public void Another_phrasing_of_an_exhausted_pool_is_a_store_failure(string message)
        => StoreFailure.Is(new InvalidOperationException(message)).ShouldBeTrue();

    [Fact]
    public void A_timeout_is_a_store_failure()
        => StoreFailure.Is(new TimeoutException("Timeout attempting to open the connection.")).ShouldBeTrue();

    [Theory]
    [InlineData("The connection is closed.")]
    [InlineData("Sequence contains no elements")]
    [InlineData("The pool of workers is full.")]
    [InlineData("")]
    public void An_invalid_operation_that_does_not_name_the_connection_pool_is_not_a_store_failure(string message)
        => StoreFailure.Is(new InvalidOperationException(message)).ShouldBeFalse();

    [Fact]
    public void An_exception_with_no_message_of_its_own_is_not_a_store_failure()
        => StoreFailure.Is(new InvalidOperationException()).ShouldBeFalse();

    [Fact]
    public void An_io_failure_is_not_a_store_failure_because_the_request_side_raises_the_same_type()
    {
        // Kestrel reports a client that went away, and a malformed request, as IOException subtypes. A provider wraps its
        // own network fault in its DbException, so the plain type carries nothing about a store.
        StoreFailure.Is(new IOException("Unable to read data from the transport connection.", new SocketException())).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(NotStoreFailures))]
    public void Other_failures_are_not_store_failures(Exception exception)
        => StoreFailure.Is(exception).ShouldBeFalse();

    public static TheoryData<Exception> NotStoreFailures => new()
    {
        new FormatException(),
        new KeyNotFoundException(),
        new NotSupportedException(),
        new HttpRequestException("Response status code does not indicate success: 503 (Service Unavailable)."),
        new OperationCanceledException(),
        new TraconException("The agent could not be compiled."),
    };

    [Fact]
    public void A_wrapped_store_failure_is_not_looked_for_inside_the_exception_that_wraps_it()
    {
        // The catalog and the host ask about the exception they caught. A TraconException is already Tracon's own,
        // normalized error; reading through it would turn a compilation error into a 503.
        StoreFailure.Is(new TraconException("The agent could not be compiled.", new ProviderException())).ShouldBeFalse();
    }

    private sealed class ProviderException : DbException
    {
        public ProviderException(string message = "injected")
            : base(message)
        {
        }
    }
}
