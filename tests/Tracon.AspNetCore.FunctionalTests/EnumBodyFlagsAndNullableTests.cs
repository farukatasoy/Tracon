using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The two enum paths of a request body that <see cref="UndefinedEnumNumberBodyTests"/> does not walk: a nullable enum,
/// and a <c>[Flags]</c> enum, which is exempt from the rule that a number must name a member.
/// </summary>
/// <remarks>
/// <para>
/// A nullable enum is read through the framework's wrapper around the converter, so the refusal of a number that names
/// no member has to survive that wrapper, and a JSON <c>null</c> must still mean "not given". The only request contract
/// that holds one is the eval-case promotion body; it answers <c>400</c> from the binding step, before the suite is
/// looked up, so a <c>404</c> there means the body was accepted.
/// </para>
/// <para>
/// No <c>[Flags]</c> enum is a field of a request contract today, so that exemption is reached only through the reader
/// every body goes through. The exemption exists because a combination of members is a number no single member has;
/// without it a valid combination would be refused as a number that names nothing.
/// </para>
/// </remarks>
public sealed class EnumBodyFlagsAndNullableTests
{
    private const string PromoteRoute = "/tracon/api/evals/no-such-suite/cases/from-run/00000000-0000-0000-0000-000000000001";

    [Theory]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("\"3\"")]
    [InlineData("\"NoSuchSource\"")]
    public async Task A_nullable_enum_that_names_no_member_is_refused_and_says_which_values_are_valid(string sourceKind)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PostPromotionAsync(host, $$"""{"sourceKind":{{sourceKind}}}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("is not a valid value.", Case.Sensitive);
        detail.ShouldContain("FailedRun, NegativeScore, ReferenceRun", Case.Sensitive);
        detail.ShouldContain("$.sourceKind", Case.Sensitive);
    }

    [Theory]
    [InlineData("""{"sourceKind":0}""")]
    [InlineData("""{"sourceKind":2}""")]
    [InlineData("""{"sourceKind":"ReferenceRun"}""")]
    [InlineData("""{"sourceKind":null}""")]
    [InlineData("""{}""")]
    public async Task A_nullable_enum_that_is_a_member_or_null_passes_the_binding_step(string body)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await PostPromotionAsync(host, body);

        // The suite does not exist: reaching its lookup is the proof that the body was read.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("""{"permissions":3}""", Permissions.Read | Permissions.Write)]
    [InlineData("""{"permissions":7}""", Permissions.Read | Permissions.Write | Permissions.Execute)]
    [InlineData("""{"permissions":"Read, Execute"}""", Permissions.Read | Permissions.Execute)]
    [InlineData("""{"permissions":0}""", Permissions.None)]
    public async Task A_flags_enum_accepts_a_combination_of_its_members_as_a_number_or_as_names(string body, Permissions expected)
    {
        var (value, error) = await ReadAsync<Holder>(body);

        error.ShouldBeNull();
        value.ShouldNotBeNull().Permissions.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"permissions":"Read, Delete"}""")]
    [InlineData("""{"permissions":"Delete"}""")]
    [InlineData("""{"permissions":true}""")]
    public async Task A_flags_enum_still_refuses_a_name_it_does_not_have(string body)
    {
        var (value, error) = await ReadAsync<Holder>(body);

        value.ShouldBeNull();
        error.ShouldNotBeNull().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        error.ProblemDetails.Detail.ShouldNotBeNull().ShouldContain("is not a valid value.", Case.Sensitive);
        error.ProblemDetails.Detail.ShouldContain("None, Read, Write, Execute", Case.Sensitive);
    }

    [Theory]
    [InlineData("""{"permissions":null}""", null)]
    [InlineData("""{"permissions":6}""", Permissions.Write | Permissions.Execute)]
    [InlineData("""{}""", null)]
    public async Task A_nullable_flags_enum_keeps_null_and_accepts_a_combination(string body, Permissions? expected)
    {
        var (value, error) = await ReadAsync<NullableHolder>(body);

        error.ShouldBeNull();
        value.ShouldNotBeNull().Permissions.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"level":0}""", Level.Low)]
    [InlineData("""{"level":"High"}""", Level.High)]
    [InlineData("""{"level":null}""", null)]
    public async Task A_nullable_plain_enum_read_by_the_body_reader_keeps_members_and_null(string body, Level? expected)
    {
        var (value, error) = await ReadAsync<LevelHolder>(body);

        error.ShouldBeNull();
        value.ShouldNotBeNull().Level.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"level":2}""")]
    [InlineData("""{"level":"2"}""")]
    [InlineData("""{"level":-5}""")]
    public async Task A_nullable_plain_enum_read_by_the_body_reader_refuses_a_number_that_names_no_member(string body)
    {
        var (value, error) = await ReadAsync<LevelHolder>(body);

        value.ShouldBeNull();
        error.ShouldNotBeNull().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        error.ProblemDetails.Detail.ShouldNotBeNull().ShouldContain("Low, High", Case.Sensitive);
    }

    private static Task<HttpResponseMessage> PostPromotionAsync(TraconTestHost host, string body)
        => host.Client.PostAsync(
            new Uri(PromoteRoute, UriKind.Relative),
            new StringContent(body, Encoding.UTF8, "application/json"));

    private static async Task<(T? Value, ProblemHttpResult? Error)> ReadAsync<T>(string json)
    {
        var services = new ServiceCollection()
            .AddOptions()
            .Configure<JsonOptions>(static options => options.SerializerOptions.PropertyNameCaseInsensitive = true)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return await RequestBodyBinding.ReadAsync<T>(context, CancellationToken.None);
    }

    [Flags]
    public enum Permissions
    {
        None = 0,
        Read = 1,
        Write = 2,
        Execute = 4,
    }

    public enum Level
    {
        Low = 0,
        High = 1,
    }

    private sealed class Holder
    {
        public Permissions Permissions { get; init; }
    }

    private sealed class NullableHolder
    {
        public Permissions? Permissions { get; init; }
    }

    private sealed class LevelHolder
    {
        public Level? Level { get; init; }
    }
}
