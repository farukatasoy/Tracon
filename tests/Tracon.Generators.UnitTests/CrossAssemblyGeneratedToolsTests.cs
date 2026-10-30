using Microsoft.CodeAnalysis;

namespace Tracon.Generators.UnitTests;

/// <summary>
/// A host registers its own generated tools beside a tool library it references
/// </summary>
/// <remarks>
/// <para>
/// The generator writes <c>AddGeneratedTools()</c> into every compilation that has a
/// tool. It was <c>public</c>, so a host that referenced a tool library saw two
/// extension methods with the same signature in the same namespace: its own and the
/// library's. Calling <c>AddGeneratedTools()</c> was <c>CS0121</c>; a host without
/// tools got <c>TRC0005</c> as well. Measured 2026-09-24 against the packed
/// <c>1.0.0-preview.2.70</c> (<c>an earlier defect</c>).
/// </para>
/// <para>
/// The extension is generated plumbing for its own compilation, so it is
/// <c>internal</c> (the answer an earlier change had planned). A library hands its tools to a
/// host through its own public extension, which calls <c>AddGeneratedTools()</c>
/// inside the library - the pattern <c>samples/Tracon.Samples.CustomTool</c> proves
/// under Native AOT.
/// </para>
/// <para>
/// Every existing generator test compiles ONE assembly, which is why none of them saw
/// the collision.
/// </para>
/// </remarks>
public sealed class CrossAssemblyGeneratedToolsTests
{
    private const string Library = """
        using System.ComponentModel;
        using Tracon;

        namespace Contoso.Tools;

        internal static class InvoiceTools
        {
            [TraconTool("total_invoice", "Totals an invoice.")]
            public static string Total([Description("The invoice number.")] string invoiceId) => invoiceId;
        }

        /// <summary>The library's own registration surface.</summary>
        public static class ContosoToolsBuilderExtensions
        {
            /// <summary>Registers the library's tools.</summary>
            public static ITraconBuilder AddContosoTools(this ITraconBuilder builder) => builder.AddGeneratedTools();
        }
        """;

    [Fact]
    public void A_host_with_its_own_tools_calls_AddGeneratedTools_beside_a_tool_library()
    {
        var library = GeneratorTestHelper.Run(Library, "Contoso.Tools");
        library.OutputErrors().ShouldBeEmpty(string.Join('\n', library.OutputErrors()));

        const string Host = """
            using System.ComponentModel;
            using Microsoft.Extensions.DependencyInjection;
            using Tracon;
            using Contoso.Tools;

            namespace MyApp;

            internal static class OrderTools
            {
                [TraconTool("get_order_status", "Returns the status of an order.")]
                public static string GetOrderStatus([Description("The order number.")] string orderId) => orderId;
            }

            internal static class Program
            {
                public static void Configure(IServiceCollection services)
                    => services.AddTracon().AddGeneratedTools().AddContosoTools();
            }
            """;

        var host = GeneratorTestHelper.Run(Host, "MyApp", library.AsReference());

        host.Diagnostics.ShouldBeEmpty();
        host.OutputErrors().ShouldBeEmpty(string.Join('\n', host.OutputErrors()));
    }

    [Fact]
    public void A_host_without_tools_registers_a_library_through_the_library_extension()
    {
        var library = GeneratorTestHelper.Run(Library, "Contoso.Tools");

        const string Host = """
            using Microsoft.Extensions.DependencyInjection;
            using Tracon;
            using Contoso.Tools;

            namespace MyApp;

            internal static class Program
            {
                public static void Configure(IServiceCollection services)
                    => services.AddTracon().AddContosoTools();
            }
            """;

        var host = GeneratorTestHelper.Run(Host, "MyApp", library.AsReference());

        host.Diagnostics.ShouldBeEmpty();
        host.OutputErrors().ShouldBeEmpty(string.Join('\n', host.OutputErrors()));
        host.GeneratedFiles().ShouldBeEmpty("a host with no tool and no AddGeneratedTools() call gets nothing generated");
    }

    [Fact]
    public void The_generated_extension_is_not_part_of_a_library_public_surface()
    {
        var library = GeneratorTestHelper.Run(Library, "Contoso.Tools");

        var extensions = library.OutputCompilation.GetTypeByMetadataName("Tracon.TraconGeneratedToolsBuilderExtensions")
            .ShouldNotBeNull();

        extensions.DeclaredAccessibility.ShouldBe(Accessibility.Internal);
    }
}
