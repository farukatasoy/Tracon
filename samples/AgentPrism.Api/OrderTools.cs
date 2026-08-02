namespace AgentPrism.Api;

/// <summary>
/// Ornek tool'lar. Gercek bir uygulamada bunlar veritabanina veya bir servise gider.
/// </summary>
/// <remarks>
/// Metotlar <c>[AgentPrismTool]</c> ile isaretlidir ve
/// <c>builder.AddAgentPrism().AddToolsFrom(typeof(OrderTools))</c> ile kaydedilir.
/// Isaretsiz metotlar tool olmaz — bu sinifa yeni bir yardimci metot eklemek onu
/// kendiliginden agent'lara acmaz. Arayuz (Faz 5) yalnizca bu listeden secim
/// yaptirir; tool kodu yazdirmaz.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Bir siparisin kargo durumunu dondurur.</summary>
    /// <param name="orderId">Siparis numarasi.</param>
    /// <returns>Kargo durumu metni.</returns>
    [AgentPrismTool("get_order_status", "Bir siparisin kargo durumunu dondurur.")]
    public static string GetOrderStatus(string orderId)
        => $"{orderId} numarali siparis kargoya verildi. Tahmini teslim: 2 gun.";

    /// <summary>Musterinin son siparislerini listeler.</summary>
    /// <param name="customerId">Musteri numarasi.</param>
    /// <returns>Siparis listesi metni.</returns>
    [AgentPrismTool("list_recent_orders", "Musterinin son siparislerini listeler.")]
    public static string ListRecentOrders(string customerId)
        => $"{customerId} musterisinin son siparisleri: ORD-1001, ORD-1002.";

    /// <summary>Bir siparisi iptal eder.</summary>
    /// <param name="orderId">Siparis numarasi.</param>
    /// <returns>Iptal sonucu metni.</returns>
    /// <remarks>
    /// <c>RequiresApproval = true</c>: bu tool geri alinamaz bir is yapar ve
    /// modelin karariyla kendiliginden calismamalidir. Isaretlenen tool defterde
    /// <c>ApprovalRequiredAIFunction</c> ile sarilir; Microsoft Agent Framework
    /// cagriyi calistirmak yerine onay istegi uretir ve arayuzde onay karti cikar.
    /// </remarks>
    [AgentPrismTool("cancel_order", "Bir siparisi iptal eder.", RequiresApproval = true)]
    public static string CancelOrder(string orderId)
        => $"{orderId} numarali siparis iptal edildi.";
}
