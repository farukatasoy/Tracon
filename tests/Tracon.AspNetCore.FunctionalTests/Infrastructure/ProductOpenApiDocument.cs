using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Gives the generated OpenAPI document the metadata a published product document
/// needs: a product title, a server entry, and the bearer scheme the endpoints use.
/// </summary>
/// <remarks>
/// <para>
/// Tracon.AspNetCore deliberately does NOT depend on
/// <c>Microsoft.AspNetCore.OpenApi</c>: a consumer's own <c>AddOpenApi()</c> call
/// produces the document, and the endpoints only carry route metadata. That is why
/// this document-level metadata cannot live in the library and is applied here, in
/// the host that generates the committed snapshot.
/// </para>
/// <para>
/// The consequence is written on the documentation site as well: in a consumer's own
/// application the title, the version, and the server list come from THAT
/// application. The snapshot describes the endpoint surface, not one deployment.
/// </para>
/// </remarks>
internal static class ProductOpenApiDocument
{
    /// <summary>The OpenAPI document name the transformer is registered against.</summary>
    /// <remarks>
    /// <c>AddOpenApi()</c> without arguments creates the document named <c>v1</c>;
    /// the options are named options, so the transformer has to use the same name.
    /// </remarks>
    public const string DocumentName = "v1";

    /// <summary>The path prefix the snapshot is generated under.</summary>
    /// <remarks>
    /// The prefix is chosen by the consumer in <c>MapTracon(prefix)</c>; the
    /// snapshot has to pick one, and this is the default the samples and the
    /// template use.
    /// </remarks>
    public const string Prefix = "/tracon";

    /// <summary>Registers the document transformer.</summary>
    /// <param name="options">The OpenAPI options of the host under test.</param>
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer(static (document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Tracon HTTP API",
                Version = "v1",
                Description =
                    "The control plane Tracon maps into an ASP.NET Core application. Every " +
                    "path below is relative to the prefix passed to MapTracon, which is " +
                    "'" + Prefix + "' in this document. The Tracon packages do not generate " +
                    "this document themselves; it is produced by calling AddOpenApi() in the " +
                    "host application, so the title, the version, and the server list in YOUR " +
                    "document come from YOUR application.",
            };

            document.Servers =
            [
                new OpenApiServer
                {
                    Url = "http://localhost:5081",
                    Description = "The address the `dotnet new tracon-api` template listens on by default.",
                },
            ];

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal)
            {
                ["bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description =
                        "The token configured through TraconEndpointOptions, or an API key " +
                        "issued from /api/keys. The header is compared in constant time. When no " +
                        "token is configured the endpoints are reachable from loopback only, so " +
                        "an unauthenticated local run still works.",
                },
            };

            document.Security =
            [
                new OpenApiSecurityRequirement
                {
                    // The reference must be bound to the host document; an unbound
                    // reference serializes as an EMPTY requirement object and the
                    // security section silently says nothing. Measured.
                    [new OpenApiSecuritySchemeReference("bearer", document)] = [],
                },
            ];

            return Task.CompletedTask;
        });
    }
}
