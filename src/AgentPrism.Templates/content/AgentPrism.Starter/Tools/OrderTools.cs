using AgentPrism;

namespace AgentPrism.Starter;

/// <summary>
/// Ornek tool. Gercek bir uygulamada bu bir veritabanina veya servise gider.
/// </summary>
/// <remarks>
/// Metot <c>[AgentPrismTool]</c> ile isaretlidir ve
/// <c>builder.AddAgentPrism().AddToolsFrom(typeof(OrderTools))</c> ile kaydedilir.
/// Isaretsiz metotlar tool olmaz — bu sinifa yeni bir yardimci metot eklemek onu
/// kendiliginden agent'lara acmaz. Tool'lar yalnizca kodda tanimlanir; arayuz
/// yalnizca bu listeden secim yaptirir.
/// </remarks>
internal static class OrderTools
{
    /// <summary>Bir siparisin kargo durumunu dondurur.</summary>
    /// <param name="orderId">Siparis numarasi.</param>
    /// <returns>Kargo durumu metni.</returns>
    [AgentPrismTool("get_order_status", "Bir siparisin kargo durumunu dondurur.")]
    public static string GetOrderStatus(string orderId)
        => $"{orderId} numarali siparis kargoya verildi. Tahmini teslim: 2 gun.";
}
