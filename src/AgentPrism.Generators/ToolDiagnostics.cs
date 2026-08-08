using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators;

/// <summary>Ureteçin urettigi tum tanilarin (APG0001-APG0007) tanimlari.</summary>
internal static class ToolDiagnostics
{
    private const string Category = "AgentPrism.Tools";

    public static readonly DiagnosticDescriptor DuplicateName = new(
        "APG0001",
        "Tool adi cakismasi",
        "'{0}' tool adi birden fazla metotta kullanilmis: {1}. Her tool adi derleme icinde tek olmalidir.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidName = new(
        "APG0002",
        "Gecersiz tool adi",
        "'{0}' metodunun tool adi '{1}' gecersiz. Tool adi 1-64 karakter olmali ve yalnizca harf, rakam, '_' veya '-' icermelidir.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        "APG0003",
        "Desteklenmeyen parametre tipi",
        "'{0}' metodunun '{1}' parametresi ('{2}' tipi) ureteç tarafindan desteklenmiyor. Desteklenen tipler: ilkel tipler, string, Guid, DateTime(Offset), enum, bunlarin dizisi/IReadOnlyList<T>'i ve CancellationToken. Baska bir tip icin 'AddTool(AIFunctionFactory.Create(...))' ile elle kaydedin.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericMethod = new(
        "APG0004",
        "Generic metot tool olamaz",
        "'{0}' metodu [AgentPrismTool] ile isaretli ancak generic. Tool metotlari generic olamaz; somut bir sarmalayici metot yazin.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoToolsFound = new(
        "APG0005",
        "Isaretli tool metodu yok",
        "'AddGeneratedTools()' cagrildi ancak bu derlemede [AgentPrismTool] ile isaretli metot yok. Tool metotlarini isaretleyin veya bu cagriyi kaldirin.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingDescription = new(
        "APG0006",
        "Tool aciklamasi eksik",
        "'{0}' tool'unun aciklamasi yok. Model tool'u ne zaman cagiracagini aciklamadan bilemez; [AgentPrismTool] icin bir aciklama verin.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InstanceMethod = new(
        "APG0007",
        "Ornek metodu tool olamaz",
        "'{0}' bir ornek metodudur ve tool olamaz. MAF, AIFunctionArguments.Services olarak bos bir saglayici gecirir (karar K-218). Metodu 'static' yapin veya tool'u kurulum aninda ornekleyip 'AddTool(AIFunctionFactory.Create(...))' ile kaydedin.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
