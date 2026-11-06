using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The server a run talks to is the major version the image of the run names.
/// </summary>
/// <remarks>
/// A leg of the platform matrix is nothing but an image name. If the name is ignored, empty, or points at another major
/// version than the leg is called after, the leg would run on the wrong server and pass without saying so, and the
/// support statement that cites the leg would rest on nothing. The image of the run (the variable, or the default image
/// when the variable is empty) carries the major version in its tag, and this case asks the server for the version it
/// really is. A tag that carries no major version fails the case instead of skipping it: a leg that cannot say which
/// version it proves proves nothing. The full version string is written to the test output so the compatibility page
/// can quote it.
/// </remarks>
public sealed partial class ServerVersionTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("pgvector/pgvector:pg12", 12)]
    [InlineData("pgvector/pgvector:pg18", 18)]
    [InlineData("pgvector/pgvector:0.8.0-pg17", 17)]
    [InlineData("registry.example/pgvector/pgvector:pg16-bookworm", 16)]
    public void The_major_version_is_read_from_the_image_tag(string image, int expected) =>
        MajorVersionOf(image).ShouldBe(expected);

    [Theory]
    [InlineData("pgvector/pgvector:latest")]
    [InlineData("pgvector/pgvector")]
    [InlineData("postgres:16-alpine")]
    [InlineData("pgvector/pgvector:pgx")]
    public void An_image_tag_without_a_major_version_is_refused(string image) =>
        Should.Throw<InvalidOperationException>(() => MajorVersionOf(image)).Message.ShouldContain(image);

    [Fact]
    public async Task The_server_is_the_major_version_the_image_names()
    {
        var image = PostgresFixture.Image;
        var expected = MajorVersionOf(image);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_setting('server_version_num')::int, current_setting('server_version');";

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        // From PostgreSQL 10 on the number is major * 10000 + minor.
        var actualMajor = reader.GetInt32(0) / 10000;
        var versionString = reader.GetString(1);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Image {image}: PostgreSQL server_version {versionString}"));

        actualMajor.ShouldBe(expected, $"The image '{image}' names PostgreSQL {expected}, the server reports {versionString}.");
    }

    /// <summary>Reads the major version from an image tag such as <c>pg12</c>, <c>pg16-bookworm</c> or <c>0.8.0-pg17</c>.</summary>
    private static int MajorVersionOf(string image)
    {
        var match = ImageTag().Match(image);

        return match.Success
            ? int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"The image '{image}' has no 'pg<major>' tag, so the leg cannot say which PostgreSQL version it runs.");
    }

    [GeneratedRegex(@":(?:[^:]*-)?pg(?<major>\d+)(?:-[^:]*)?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageTag();
}
