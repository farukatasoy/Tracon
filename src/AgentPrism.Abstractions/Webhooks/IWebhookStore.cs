namespace AgentPrism;

/// <summary>Webhook aboneliklerinin ve teslim gecmisinin deposu.</summary>
public interface IWebhookStore
{
    /// <summary>Bir kiracinin aboneliklerini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Abonelikler.</returns>
    ValueTask<IReadOnlyList<WebhookSubscription>> ListSubscriptionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir aboneligi adiyla getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Abonelik adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Abonelik; yoksa <see langword="null"/>.</returns>
    ValueTask<WebhookSubscription?> GetSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirli bir olaya abone olan ve etkin olan tum abonelikleri getirir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="eventType">Olay adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Eslesen abonelikler. Yoksa bos liste.</returns>
    ValueTask<IReadOnlyList<WebhookSubscription>> FindForEventAsync(
        string tenantId,
        string eventType,
        CancellationToken cancellationToken = default);

    /// <summary>Bir aboneligi olusturur veya gunceller.</summary>
    /// <param name="subscription">Abonelik.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaydedilen abonelik.</returns>
    ValueTask<WebhookSubscription> SaveSubscriptionAsync(
        WebhookSubscription subscription,
        CancellationToken cancellationToken = default);

    /// <summary>Bir aboneligi ve teslim gecmisini siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Abonelik adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteSubscriptionAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir aboneligin ust uste basarisizlik sayacini gunceller ve esik asilirsa
    /// aboneligi devre disi birakir.
    /// </summary>
    /// <param name="subscriptionId">Abonelik kimligi.</param>
    /// <param name="succeeded">Son teslim basarili mi.</param>
    /// <param name="disableThreshold">
    /// Bu sayida ust uste basarisizliktan sonra abonelik kapatilir.
    /// </param>
    /// <param name="updatedAt">Guncelleme zamani (UTC).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Abonelik bu cagri sirasinda kapandiysa <see langword="true"/>.</returns>
    ValueTask<bool> RecordSubscriptionOutcomeAsync(
        Guid subscriptionId,
        bool succeeded,
        int disableThreshold,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Bir teslim kaydi olusturur.</summary>
    /// <param name="delivery">Teslim.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Olusturulan kayit.</returns>
    ValueTask<WebhookDelivery> CreateDeliveryAsync(
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir teslim kaydini getirir.</summary>
    /// <param name="deliveryId">Teslim kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa <see langword="null"/>.</returns>
    ValueTask<WebhookDelivery?> GetDeliveryAsync(
        Guid deliveryId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir teslim denemesinin sonucunu yazar.</summary>
    /// <param name="result">Sonuc.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask RecordDeliveryResultAsync(
        WebhookDeliveryResult result,
        CancellationToken cancellationToken = default);

    /// <summary>Teslim gecmisini listeler. En yeni kayit basta doner.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    ValueTask<IReadOnlyList<WebhookDelivery>> QueryDeliveriesAsync(
        WebhookDeliveryQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Bir olayi ilgili aboneliklere yayan bilesen.
/// </summary>
/// <remarks>
/// <para>
/// Cekirdek kod bunu cagirir; teslim <strong>istek icinde yapilmaz</strong>,
/// kuyruga yazilir. Boylece yavas veya erisilemeyen bir alici ana yolu
/// yavaslatmaz.
/// </para>
/// <para>
/// Gozlemlenebilirlik kurali burada da gecerlidir: yayin basarisiz olursa
/// calistirma <strong>devam eder</strong>, hata yalnizca loglanir.
/// </para>
/// </remarks>
public interface IWebhookPublisher
{
    /// <summary>Bir olayi kuyruga yazar.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="eventType">Olay adi. Bkz. <see cref="WebhookEvents"/>.</param>
    /// <param name="payload">Olay ozeti. Serilestirilerek govdeye yazilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Olusturulan teslim sayisi. Abone yoksa 0.</returns>
    ValueTask<int> PublishAsync(
        string tenantId,
        string eventType,
        WebhookEventPayload payload,
        CancellationToken cancellationToken = default);
}
