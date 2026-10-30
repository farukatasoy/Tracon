using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon.Cli.Commands;

/// <summary>
/// Plans, validates and, with <c>--apply</c>, writes a <c>tracon.definitions</c> file into a Tracon installation.
/// </summary>
/// <remarks>
/// <para>
/// A call without <c>--apply</c> is a dry run: it writes nothing and says, item by item, what would happen. That is the
/// default on purpose: the command that writes is the one that has to be asked for.
/// </para>
/// <para>
/// Exit codes: <c>0</c> = the file is valid (dry run) or was written; <c>1</c> = argument error; <c>2</c> = could not
/// run (connection, HTTP, a store that failed while writing: running the command again is the right move);
/// <c>3</c> = the file is invalid or an item is blocked (running it again, unchanged, is the wrong move).
/// </para>
/// </remarks>
internal static class ImportCommand
{
    private const int MaxChangedPathsShown = 8;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var file = CliArgs.RequireOption(args, "--file");
        var types = CliArgs.GetOption(args, "--types");
        var apply = CliArgs.HasFlag(args, "--apply");
        var asJson = CliArgs.HasFlag(args, "--json");

        if (!File.Exists(file))
        {
            throw new CliArgumentException($"File '{file}' does not exist.");
        }

        var body = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);

        using var client = DefinitionTransferHttp.CreateClient(args);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(DefinitionTransferHttp.Budget);

        var query = $"dryRun={(apply ? "false" : "true")}" + (types is null ? string.Empty : "&types=" + Uri.EscapeDataString(types));

        try
        {
            using var content = new ByteArrayContent(body);

            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var response = await client
                .PostAsync(new Uri("api/definitions/import?" + query, UriKind.Relative), content, timeout.Token)
                .ConfigureAwait(false);

            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (TryReadReport(text) is { } report)
            {
                Print(report, text, asJson);

                return ExitCode(report);
            }

            // Not a report: a problem. The title and the detail are the server's own words and carry no secret.
            using var problem = new HttpResponseMessage(response.StatusCode) { Content = new StringContent(text) };

            Console.Error.WriteLine(await DefinitionTransferHttp.DescribeProblemAsync(problem, timeout.Token).ConfigureAwait(false));

            return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType
                ? 3
                : 2;
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
    }

    private static JsonObject? TryReadReport(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject { } report && report["items"] is JsonArray ? report : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ExitCode(JsonObject report)
    {
        if (report["valid"]?.GetValue<bool>() != true)
        {
            return 3;
        }

        var dryRun = report["dryRun"]?.GetValue<bool>() == true;

        // A valid file that was meant to be written and was not (a store failed, or failed half way) is a run that
        // did not finish: running it again writes the rest.
        return dryRun || report["applied"]?.GetValue<bool>() == true ? 0 : 2;
    }

    private static void Print(JsonObject report, string raw, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(raw);

            return;
        }

        var dryRun = report["dryRun"]?.GetValue<bool>() == true;

        if (dryRun)
        {
            Console.WriteLine("Dry run: nothing is written.");
        }

        foreach (var node in report["items"]!.AsArray())
        {
            if (node is JsonObject item)
            {
                PrintItem(item);
            }
        }

        if (report["valid"]?.GetValue<bool>() != true)
        {
            Console.WriteLine("The file cannot be imported as it is. Nothing was written.");

            return;
        }

        if (dryRun)
        {
            Console.WriteLine("The file is valid. Run again with --apply to write it.");

            return;
        }

        var written = report["written"] is JsonArray list ? list.Count : 0;

        Console.WriteLine(
            report["applied"]?.GetValue<bool>() == true
                ? string.Create(CultureInfo.InvariantCulture, $"Written: {written} item(s).")
                : report["partial"]?.GetValue<bool>() == true
                    ? string.Create(CultureInfo.InvariantCulture, $"Stopped after {written} item(s) were written. Run the same command again to write the rest.")
                    : "Nothing was written.");
    }

    private static void PrintItem(JsonObject item)
    {
        var action = item["action"]?.GetValue<string>() ?? "?";
        var type = item["type"]?.GetValue<string>() ?? "?";
        var name = item["name"]?.GetValue<string>() ?? "?";
        var versions = item["sourceVersion"] is { } source && item["targetVersion"] is { } target
            ? string.Create(CultureInfo.InvariantCulture, $"  v{source} -> v{target}")
            : string.Empty;

        Console.WriteLine($"{action,-10} {type,-9} {name}{versions}");

        if (item["changedPaths"] is JsonArray paths)
        {
            foreach (var path in paths.Take(MaxChangedPathsShown))
            {
                Console.WriteLine($"             ~ {path?.GetValue<string>()}");
            }

            if (paths.Count > MaxChangedPathsShown)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"             ... and {paths.Count - MaxChangedPathsShown} more"));
            }
        }

        if (item["messages"] is JsonArray messages)
        {
            foreach (var message in messages.OfType<JsonObject>())
            {
                var path = message["path"]?.GetValue<string>();

                Console.WriteLine(
                    $"             {(message["severity"]?.GetValue<string>() ?? "?").ToUpperInvariant()} [{message["code"]?.GetValue<string>()}] " +
                    $"{message["message"]?.GetValue<string>()}{(path is null ? string.Empty : $" ({path})")}");
            }
        }
    }
}
