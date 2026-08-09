using System.ComponentModel;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>Testlerde kullanilan tek tool.</summary>
/// <remarks>
/// Tool'lar yalnizca kodda tanimlanir. Bu sinif, arayuzun tool listesini ve tool
/// kartini dogrulayabilmesi icin gercek bir kayit saglar.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Bir siparisin durumunu dondurur.</summary>
    /// <param name="orderId">Siparis numarasi.</param>
    /// <returns>Insan tarafindan okunabilir durum metni.</returns>
    [AgentPrismTool("get_order_status", "Bir siparisin kargo durumunu dondurur.")]
    [Description("Bir siparisin kargo durumunu dondurur.")]
    public static string GetOrderStatus([Description("Siparis numarasi")] string orderId)
        => $"{orderId} siparisi kargoya verildi.";

    /// <summary>Bir siparisi iptal eder. Onay ister (Faz 55 E2E testi icin).</summary>
    /// <param name="orderId">Siparis numarasi.</param>
    /// <returns>Insan tarafindan okunabilir sonuc metni.</returns>
    [AgentPrismTool("cancel_order", "Bir siparisi iptal eder.", RequiresApproval = true)]
    [Description("Bir siparisi iptal eder.")]
    public static string CancelOrder([Description("Siparis numarasi")] string orderId)
        => $"{orderId} siparisi iptal edildi.";
}
