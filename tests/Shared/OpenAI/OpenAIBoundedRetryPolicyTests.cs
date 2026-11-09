using System.ClientModel.Primitives;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Tracon.Tests.Common;

namespace Tracon.OpenAIShared.Tests;

/// <summary>
/// The wait the OpenAI clients' retry policy makes between two tries: it runs on the clock the policy was given,
/// a <c>Retry-After</c> above the ceiling is replaced by the library's backoff, and the caller's cancellation ends it.
/// </summary>
/// <remarks>
/// <para>
/// The policy is driven through a real client pipeline whose transport answers from a script, so the retry loop is the
/// library's own. The clock is a <see cref="ManualClock"/>: a test never waits for a delay and never measures one. What
/// it reads is the timer the wait armed (<see cref="ManualClock.Pending"/>) and the steps the clock took to carry the
/// call to its end (<see cref="ManualClock.Steps"/>).
/// </para>
/// <para>
/// Linked into <c>Tracon.OpenAI.UnitTests</c> and <c>Tracon.Azure.UnitTests</c>: the source under test is compiled into
/// both packages. Every test runs on both retry loops of the library, the asynchronous one and the synchronous one,
/// because the policy overrides one wait for each.
/// </para>
/// </remarks>
public sealed class OpenAIBoundedRetryPolicyTests
{
    private static readonly Uri Address = new("http://provider.invalid/v1/chat");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_wait_before_a_retry_ends_only_when_the_clock_passes_the_delay(bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((429, "2"), (200, null));
        var send = Start(transport, clock, synchronous, CancellationToken.None);

        await WaitUntil.TrueAsync(() => clock.Pending.Count == 1, "the retry to arm its wait on the clock");

        clock.Pending.ShouldBe([TimeSpan.FromSeconds(2)], "the header's two seconds, armed on the clock the policy was given");
        transport.Calls.ShouldBe(1, "the retry is not sent while the wait is open");

        clock.Advance(TimeSpan.FromSeconds(1.9));

        clock.Pending.ShouldBe([TimeSpan.FromSeconds(0.1)], "the clock has not reached the delay");
        transport.Calls.ShouldBe(1);

        await clock.AdvanceUntilAsync(send);

        transport.Calls.ShouldBe(2, "one 429 and the retry that answered");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retry_after_within_the_ceiling_is_obeyed(bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((429, "2"), (200, null));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous, CancellationToken.None));

        clock.Steps.ShouldBe([TimeSpan.FromSeconds(2)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_retry_after_of_exactly_the_ceiling_is_obeyed(bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((429, "60"), (200, null));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous, CancellationToken.None));

        clock.Steps.ShouldBe([ProviderRetryAfter.MaximumHonoredDelay]);
    }

    [Theory]
    [InlineData("61", false)]
    [InlineData("86400", false)]
    [InlineData("2147483647", false)]
    [InlineData("Fri, 31 Dec 9999 23:59:59 GMT", false)]
    [InlineData("86400", true)]
    [InlineData("Fri, 31 Dec 9999 23:59:59 GMT", true)]
    public async Task A_retry_after_above_the_ceiling_is_replaced_by_the_library_backoff(string retryAfter, bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((429, retryAfter), (200, null));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous, CancellationToken.None));

        clock.Steps.ShouldBe([TimeSpan.FromSeconds(0.8)], $"Retry-After '{retryAfter}' must give way to the first backoff step");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_provider_that_keeps_refusing_is_retried_three_times_at_the_library_backoff(bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((429, "86400"));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous, CancellationToken.None));

        clock.Steps.ShouldBe([TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(3.2)]);
        transport.Calls.ShouldBe(4, "the first try and the library's three retries");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_token_that_is_already_cancelled_ends_the_wait_without_arming_a_timer(bool synchronous)
    {
        // Through a pipeline the library throws on a cancelled token before it ever reaches the wait, so the wait is
        // called the way the library calls it: with the delay and the message's token.
        var clock = new ManualClock();
        var policy = new OpenAIBoundedRetryPolicy(clock);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var wait = synchronous
            ? Task.Run(() => Invoke(policy, "Wait", TimeSpan.FromSeconds(2), cancellation.Token))
            : (Task)Invoke(policy, "WaitAsync", TimeSpan.FromSeconds(2), cancellation.Token)!;

        await AssertCancelledAsync(wait);

        clock.Pending.ShouldBeEmpty("a wait that begins cancelled arms nothing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_token_cancelled_during_the_wait_ends_it_and_drops_the_timer(bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((429, "2"), (200, null));
        using var cancellation = new CancellationTokenSource();
        var send = Start(transport, clock, synchronous, cancellation.Token);

        await WaitUntil.TrueAsync(() => clock.Pending.Count == 1, "the retry to arm its wait on the clock");

        await cancellation.CancelAsync();

        await AssertCancelledAsync(send);

        clock.Pending.ShouldBeEmpty("the wait that was cancelled disposed its timer");
        clock.Steps.ShouldBeEmpty("the clock never moved");
        transport.Calls.ShouldBe(1, "the retry is not sent after the caller gave up");
    }

    // The call ends by itself when the policy honors the token; one that ignored it would wait on a clock nobody moves,
    // so the end is awaited as a condition (bounded as a failure) before the outcome is read.
    private static async Task AssertCancelledAsync(Task send)
    {
        await WaitUntil.TrueAsync(() => send.IsCompleted, "the call to end once its token was cancelled");

        var thrown = await Record.ExceptionAsync(() => send);

        thrown.ShouldBeAssignableTo<OperationCanceledException>("the caller's cancellation ends the call as a cancellation, not as a failure of the provider");
    }

    // The policy's waits are protected overrides of the library's retry policy and the policy is sealed, so the only way to
    // call one on its own is the way the library does: by name, with the delay and the token.
    private static object? Invoke(OpenAIBoundedRetryPolicy policy, string name, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            return typeof(OpenAIBoundedRetryPolicy)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, [typeof(TimeSpan), typeof(CancellationToken)])
                .ShouldNotBeNull($"the library's {name} no longer has this shape")
                .Invoke(policy, [delay, cancellationToken]);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(failure.InnerException).Throw();

            throw;
        }
    }

    private static Task Start(Script transport, TimeProvider clock, bool synchronous, CancellationToken cancellationToken)
    {
        var pipeline = ClientPipeline.Create(new ClientPipelineOptions
        {
            RetryPolicy = new OpenAIBoundedRetryPolicy(clock),
            Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
        });
        var message = pipeline.CreateMessage(Address, "GET");
        message.Apply(new RequestOptions { CancellationToken = cancellationToken });

        // The synchronous loop blocks its thread in the policy's wait, so it runs on one of its own.
        return synchronous
            ? Task.Run(() => pipeline.Send(message), CancellationToken.None)
            : pipeline.SendAsync(message).AsTask();
    }

    /// <summary>A transport that answers from a list, repeating the last answer when the list runs out.</summary>
    private sealed class Script(params (int Status, string? RetryAfter)[] answers) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Next());

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => Next();

        private HttpResponseMessage Next()
        {
            var (status, retryAfter) = answers[Math.Min(Interlocked.Increment(ref _calls), answers.Length) - 1];
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}") };

            if (retryAfter is not null)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            }

            return response;
        }
    }
}
