using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The server a run talks to is the release the image of the run names.
/// </summary>
/// <remarks>
/// A leg of the platform matrix is nothing but an image name. If the name is ignored, empty, or points at another release
/// than the leg is called after, the leg would run on the wrong server and pass without saying so, and the support
/// statement that cites the leg would rest on nothing. The image of the run (the variable, or the default image when the
/// variable is empty) carries the release year in its tag, and this case asks the server for the major version it really
/// is. A tag that carries no known release year fails the case instead of skipping it: a leg that cannot say which version
/// it proves proves nothing. The full version string is written to the test output so the compatibility page can quote it.
/// </remarks>
public sealed partial class ServerVersionTests(SqlServerFixture fixture)
{
    /// <summary>The release year of the image tag and the major version the server reports for it.</summary>
    private static readonly Dictionary<int, int> MajorByYear = new()
    {
        [2017] = 14,
        [2019] = 15,
        [2022] = 16,
        [2025] = 17,
    };

    [Theory]
    [InlineData("mcr.microsoft.com/mssql/server:2017-latest", 14)]
    [InlineData("mcr.microsoft.com/mssql/server:2019-latest", 15)]
    [InlineData("mcr.microsoft.com/mssql/server:2022-latest", 16)]
    [InlineData("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04", 16)]
    [InlineData("mcr.microsoft.com/mssql/server:2025-latest", 17)]
    public void The_major_version_is_read_from_the_image_tag(string image, int expected) =>
        MajorVersionOf(image).ShouldBe(expected);

    [Theory]
    [InlineData("mcr.microsoft.com/mssql/server:latest")]
    [InlineData("mcr.microsoft.com/mssql/server")]
    [InlineData("mcr.microsoft.com/mssql/server:2016-latest")]
    [InlineData("mcr.microsoft.com/mssql/server:20190-latest")]
    public void An_image_tag_without_a_known_release_year_is_refused(string image) =>
        Should.Throw<InvalidOperationException>(() => MajorVersionOf(image)).Message.ShouldContain(image);

    [Fact]
    public async Task The_server_is_the_release_the_image_names()
    {
        var image = SqlServerFixture.Image;
        var expected = MajorVersionOf(image);

        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion')), CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')), CONVERT(nvarchar(128), SERVERPROPERTY('ProductLevel')), CONVERT(nvarchar(128), SERVERPROPERTY('ProductUpdateLevel'));";

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        var actualMajor = reader.GetInt32(0);
        var version = reader.GetString(1);
        var level = reader.GetString(2);
        var update = reader.IsDBNull(3) ? "no update level" : reader.GetString(3);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Image {image}: SQL Server {version} ({level}, {update})"));

        actualMajor.ShouldBe(expected, $"The image '{image}' names major version {expected}, the server reports {version}.");
    }

    /// <summary>Reads the release year from an image tag such as <c>2019-latest</c> and maps it to the server's major version.</summary>
    private static int MajorVersionOf(string image)
    {
        var match = ImageTag().Match(image);

        return match.Success && MajorByYear.TryGetValue(int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture), out var major)
            ? major
            : throw new InvalidOperationException($"The image '{image}' has no tag that starts with a known SQL Server release year ({string.Join(", ", MajorByYear.Keys)}), so the leg cannot say which version it runs.");
    }

    [GeneratedRegex(@":(?<year>\d{4})(?:-[^:]*)?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageTag();
}
