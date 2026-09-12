using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Client;
using Tracon.Client.Generated;

namespace Tracon.Cli.Commands;

/// <summary>
/// Reads model provider health over HTTP, through <see cref="TraconApiClient"/>.
/// </summary>
internal static class HealthCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var url = CliArgs.RequireOption(args, "--url");
        var token = CliArgs.GetOption(args, "--token") ?? Environment.GetEnvironmentVariable("TRACON_TOKEN");
        var asJson = CliArgs.HasFlag(args, "--json");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
        {
            throw new CliArgumentException("'--url' must be an absolute URL, for example http://localhost:5080/tracon.");
        }

        var services = new ServiceCollection();
        services.AddTraconClient(options =>
        {
            options.BaseAddress = baseAddress;
            options.Token = token;
        });

        // await using var x = ...; puts ConfigureAwait(false) out of reach for
        // the compiler-generated dispose call (MA0004); declaring the variable
        // first and wrapping usage in `await using (x.ConfigureAwait(false))`
        // keeps the concrete type usable. docs/hafiza/build-ve-analyzer.md.
        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var client = provider.GetRequiredService<TraconApiClient>();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(Timeout);

            try
            {
                var health = await client.TraconModelsHealthAsync(refresh: null, timeoutSource.Token).ConfigureAwait(false);
                Print(health, asJson);
                return 0;
            }
            catch (TraconApiException ex)
            {
                // ex.Message embeds the response body; print only the status code
                // to keep the promise that CLI output carries no server secret (K-059).
                Console.Error.WriteLine($"Request failed: HTTP {ex.StatusCode}.");
                return 2;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"Timed out after {Timeout.TotalSeconds:0} s.");
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
    }

    private static void Print(ICollection<Tracon.Client.Generated.ModelProviderHealth> health, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(health, PrettyJson));
            return;
        }

        if (health.Count == 0)
        {
            Console.WriteLine("No model providers registered.");
            return;
        }

        foreach (var providerHealth in health)
        {
            var detail = string.IsNullOrEmpty(providerHealth.Detail) ? string.Empty : $" ({providerHealth.Detail})";
            Console.WriteLine($"{providerHealth.ProviderName}: {providerHealth.Status}{detail}");
        }
    }
}
