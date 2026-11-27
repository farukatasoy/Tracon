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
    public async Task A_token_that_is_already_cancelled_ends_the_wait_and_leaves_no_timer_armed(bool synchronous)
    {
        // Through a pipeline the library throws on a cancelled token before it ever reaches the wait, so the wait is
        // called the way the library calls it: with the delay and the message's token. The asynchronous wait never arms a
        // timer for a cancelled token; the blocking one arms its timer before it sees the token and drops it on the way out.
        var clock = new ManualClock();
        var policy = new OpenAIBoundedRetryPolicy(clock);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var wait = synchronous
            ? Task.Run(() => Invoke(policy, "Wait", TimeSpan.FromSeconds(2), cancellation.Token))
            : (Task)Invoke(policy, "WaitAsync", TimeSpan.FromSeconds(2), cancellation.Token)!;

        await AssertCancelledAsync(wait);

        clock.Pending.ShouldBeEmpty("a wait that begins cancelled leaves no timer armed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_wait_on_the_system_clock_is_the_library_s_own_and_still_ends_on_cancellation(bool synchronous)
    {
        // The default clock is the system's, and then both waits are the library's. The delay is an hour: only the
        // token can end the call, and it is already cancelled, so nothing here waits.
        var policy = new OpenAIBoundedRetryPolicy();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var wait = synchronous
            ? Task.Run(() => Invoke(policy, "Wait", TimeSpan.FromHours(1), cancellation.Token))
            : (Task)Invoke(policy, "WaitAsync", TimeSpan.FromHours(1), cancellation.Token)!;

        await AssertCancelledAsync(wait);
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

    /// <summary>
    /// The number of tries is the limit plus the first one. With no limit asked for it is three retries, the documented
    /// default, so a change to the number is a change a test shows and a release note names.
    /// </summary>
    [Theory]
    [InlineData(null, 4, false)]
    [InlineData(null, 4, true)]
    [InlineData(0, 1, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(5, 6, false)]
    public async Task A_call_that_keeps_failing_is_sent_once_and_then_as_often_as_the_limit_allows(int? maxRetries, int expectedCalls, bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((503, null));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous, CancellationToken.None, maxRetries));

        transport.Calls.ShouldBe(expectedCalls);
        OpenAIBoundedRetryPolicy.LibraryDefaultMaxRetries.ShouldBe(3);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    public async Task A_status_the_library_does_not_retry_is_sent_once_whatever_the_limit(int status)
    {
        var clock = new ManualClock();
        var transport = new Script((status, null));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous: false, CancellationToken.None, maxRetries: 5));

        transport.Calls.ShouldBe(1);
    }

    /// <summary>
    /// A ceiling set on one message lowers the retries of that message below the policy's own count, and never raises them.
    /// The count the library keeps on the message is not visible to a derived policy, so the policy counts its own retries:
    /// this theory is what notices a library that stops calling the hook it counts on.
    /// </summary>
    [Theory]
    [InlineData(null, 0, 1, false)]
    [InlineData(null, 0, 1, true)]
    [InlineData(null, 1, 2, false)]
    [InlineData(null, 1, 2, true)]
    [InlineData(null, 2, 3, false)]
    [InlineData(null, 3, 4, false)]
    [InlineData(null, 10, 4, false)]
    [InlineData(null, 10, 4, true)]
    [InlineData(1, 5, 2, false)]
    [InlineData(1, 5, 2, true)]
    [InlineData(0, 5, 1, false)]
    [InlineData(5, 2, 3, false)]
    [InlineData(5, 2, 3, true)]
    public async Task A_ceiling_on_the_message_lowers_the_retries_and_never_raises_them(int? maxRetries, int ceiling, int expectedCalls, bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((503, null));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous, CancellationToken.None, maxRetries, ceiling));

        transport.Calls.ShouldBe(expectedCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_call_that_succeeds_within_the_ceiling_is_not_cut_short(bool synchronous)
    {
        var clock = new ManualClock();
        var transport = new Script((503, null), (200, null));

        await clock.AdvanceUntilAsync(Start(transport, clock, synchronous, CancellationToken.None, maxRetries: null, ceiling: 1));

        transport.Calls.ShouldBe(2, "the first try failed, the one retry the ceiling allows answered");
    }

    [Fact]
    public async Task A_ceiling_belongs_to_its_message_and_the_next_message_of_the_pipeline_keeps_the_policys_count()
    {
        var clock = new ManualClock();
        var transport = new Script((503, null));
        var pipeline = ClientPipeline.Create(new ClientPipelineOptions
        {
            RetryPolicy = new OpenAIBoundedRetryPolicy(clock),
            Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
        });

        var capped = pipeline.CreateMessage(Address, "GET");
        OpenAIBoundedRetryPolicy.CapRetries(capped, 0);
        await clock.AdvanceUntilAsync(pipeline.SendAsync(capped).AsTask());

        transport.Calls.ShouldBe(1, "the capped message is sent once");

        var plain = pipeline.CreateMessage(Address, "GET");
        await clock.AdvanceUntilAsync(pipeline.SendAsync(plain).AsTask());

        transport.Calls.ShouldBe(1 + 4, "a message with no ceiling is sent the first time and the policy's three retries");
    }

    /// <summary>
    /// Messages with different ceilings that are in flight on one pipeline at the same time each keep their own count. The
    /// policy is one object shared by every message of the client, so a count kept on the policy instead of on the message
    /// would let one message's retries use up another's: with ceilings of one and three, the one that may retry three times
    /// would stop after two.
    /// </summary>
    [Fact]
    public async Task Messages_with_different_ceilings_in_flight_on_one_pipeline_each_keep_their_own_count()
    {
        var clock = new ManualClock();
        var transport = new Script((503, null));
        var pipeline = ClientPipeline.Create(new ClientPipelineOptions
        {
            RetryPolicy = new OpenAIBoundedRetryPolicy(clock),
            Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
        });

        (int Ceiling, int ExpectedCalls)[] expectations = [(0, 1), (1, 2), (3, 4)];
        var sends = new List<Task>();

        foreach (var (ceiling, _) in expectations)
        {
            var message = pipeline.CreateMessage(new Uri($"http://provider.invalid/v1/ceiling-{ceiling}"), "GET");
            OpenAIBoundedRetryPolicy.CapRetries(message, ceiling);
            sends.Add(pipeline.SendAsync(message).AsTask());
        }

        // The message that may not retry has ended; the other two are each waiting out their first backoff at once.
        await WaitUntil.TrueAsync(() => clock.Pending.Count == 2, "both messages that may retry to be waiting on the clock together");

        await clock.AdvanceUntilAsync(Task.WhenAll(sends));

        foreach (var (ceiling, expectedCalls) in expectations)
        {
            transport.CallsTo($"/v1/ceiling-{ceiling}").ShouldBe(expectedCalls, $"the message with a ceiling of {ceiling} is sent once and then as often as its own ceiling allows");
        }
    }

    [Fact]
    public void A_negative_ceiling_is_refused()
    {
        var pipeline = ClientPipeline.Create();
        var message = pipeline.CreateMessage(Address, "GET");

        Should.Throw<ArgumentOutOfRangeException>(() => OpenAIBoundedRetryPolicy.CapRetries(message, -1));
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

    private static Task Start(Script transport, TimeProvider clock, bool synchronous, CancellationToken cancellationToken, int? maxRetries = null, int? ceiling = null)
    {
        var pipeline = ClientPipeline.Create(new ClientPipelineOptions
        {
            RetryPolicy = new OpenAIBoundedRetryPolicy(clock, maxRetries),
            Transport = new HttpClientPipelineTransport(new HttpClient(transport)),
        });
        var message = pipeline.CreateMessage(Address, "GET");
        message.Apply(new RequestOptions { CancellationToken = cancellationToken });

        if (ceiling is { } cap)
        {
            OpenAIBoundedRetryPolicy.CapRetries(message, cap);
        }

        // The synchronous loop blocks its thread in the policy's wait, so it runs on one of its own.
        // The token is on the message already (`Apply` above). System.ClientModel 1.17 has a `SendAsync(message, token)`
        // overload that only assigns it, and this file is compiled where 1.16, which has none, is restored too.
#pragma warning disable CA2016
        return synchronous
            ? Task.Run(() => pipeline.Send(message), CancellationToken.None)
            : pipeline.SendAsync(message).AsTask();
#pragma warning restore CA2016
    }

    /// <summary>A transport that answers from a list, repeating the last answer when the list runs out.</summary>
    private sealed class Script(params (int Status, string? RetryAfter)[] answers) : HttpMessageHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _callsByPath = new(StringComparer.Ordinal);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Gets how many requests reached the transport for one path.</summary>
        public int CallsTo(string path) => _callsByPath.TryGetValue(path, out var calls) ? calls : 0;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Record(request);

            return Task.FromResult(Next());
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Record(request);

            return Next();
        }

        private void Record(HttpRequestMessage request)
            => _callsByPath.AddOrUpdate(request.RequestUri!.AbsolutePath, 1, static (_, calls) => calls + 1);

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
