namespace AgentPrism.Api;

/// <summary>
/// Ornek tool'lar. Gercek bir uygulamada bunlar veritabanina veya bir servise gider.
/// </summary>
/// <remarks>
/// Bu metotlar <c>builder.AddAgentPrism().AddTool(...)</c> ile kaydedilir.
/// Arayuz (Faz 5) yalnizca bu listeden secim yaptirir; tool kodu yazdirmaz.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Bir siparisin kargo durumunu dondurur.</summary>
    /// <param name="orderId">Siparis numarasi.</param>
    /// <returns>Kargo durumu metni.</returns>
    public static string GetOrderStatus(string orderId)
        => $"{orderId} numarali siparis kargoya verildi. Tahmini teslim: 2 gun.";

    /// <summary>Musterinin son siparislerini listeler.</summary>
    /// <param name="customerId">Musteri numarasi.</param>
    /// <returns>Siparis listesi metni.</returns>
    public static string ListRecentOrders(string customerId)
        => $"{customerId} musterisinin son siparisleri: ORD-1001, ORD-1002.";
}
