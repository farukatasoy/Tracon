using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon.Cli.Commands;

/// <summary>
/// Writes the definitions of a Tracon installation as a <c>tracon.definitions</c> file.
/// </summary>
/// <remarks>
/// The file goes to <c>--output</c>, or to stdout when there is none; everything the command says about it goes to
/// stderr, so <c>tracon export --url ... &gt; bundle.json</c> leaves a file that is only the file.
/// </remarks>
internal static class ExportCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var agents = CliArgs.GetOptions(args, "--agent");
        var workflows = CliArgs.GetOptions(args, "--workflow");
        var types = CliArgs.GetOption(args, "--types");
        var output = CliArgs.GetOption(args, "--output");

        if ((agents.Count > 0 || workflows.Count > 0) && types is not null)
        {
            throw new CliArgumentException("Name definitions ('--agent', '--workflow') or kinds ('--types'), not both.");
        }

        var query = new List<string>();

        query.AddRange(agents.Select(static name => "agent=" + Uri.EscapeDataString(name)));
        query.AddRange(workflows.Select(static name => "workflow=" + Uri.EscapeDataString(name)));

        if (types is not null)
        {
            query.Add("types=" + Uri.EscapeDataString(types));
        }

        using var client = DefinitionTransferHttp.CreateClient(args);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(DefinitionTransferHttp.Budget);

        try
        {
            using var response = await client
                .GetAsync(new Uri("api/definitions/export" + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query)), UriKind.Relative), timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(await DefinitionTransferHttp.DescribeProblemAsync(response, timeout.Token).ConfigureAwait(false));

                return 2;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);

            if (output is null)
            {
                // `await using var` puts ConfigureAwait out of reach of the compiler-generated dispose (MA0004).
                var stdout = Console.OpenStandardOutput();

                await using (stdout.ConfigureAwait(false))
                {
                    await stdout.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
                }
            }
            else
            {
                await File.WriteAllBytesAsync(output, bytes, timeout.Token).ConfigureAwait(false);
            }

            Report(bytes, output);

            return 0;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine($"Timed out after {DefinitionTransferHttp.Budget.TotalSeconds:0} s.");

            return 2;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine(
                $"The request was broken off by the transport before the {DefinitionTransferHttp.Budget.TotalSeconds:0} s budget elapsed.");

            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");

            return 2;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Connection failed: {ex.Message}");

            return 2;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Could not write the file: {ex.Message}");

            return 2;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Could not write the file: {ex.Message}");

            return 2;
        }
    }

    private static void Report(byte[] bytes, string? output)
    {
        var summary = new StringBuilder();

        try
        {
            if (JsonNode.Parse(bytes) is JsonObject bundle)
            {
                summary.Append(
                    CultureInfo.InvariantCulture,
                    $"{Count(bundle, "agents")} agent(s), {Count(bundle, "workflows")} workflow(s), {Count(bundle, "skills")} skill(s), {Count(bundle, "mcpServers")} MCP server(s)");

                if (bundle["warnings"] is JsonArray warnings)
                {
                    foreach (var warning in warnings)
                    {
                        Console.Error.WriteLine($"warning: {warning?.GetValue<string>()}");
                    }
                }
            }
        }
        catch (JsonException)
        {
            summary.Append("a file the CLI could not read back");
        }

        Console.Error.WriteLine(output is null ? $"Wrote {summary} to standard output." : $"Wrote {summary} to {output}.");
    }

    private static int Count(JsonObject bundle, string member) => bundle[member] is JsonArray items ? items.Count : 0;
}
