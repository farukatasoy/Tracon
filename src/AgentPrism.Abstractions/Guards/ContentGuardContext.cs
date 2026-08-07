namespace AgentPrism;

/// <summary>Bir icerik denetiminin yonu.</summary>
/// <remarks>
/// Deger JSON'a yazilmaz ve veritabaninda saklanmaz; yalnizca calistirma olayinin
/// metninde ve denetim izinde ad olarak gorunur.
/// </remarks>
public enum ContentGuardDirection
{
    /// <summary>Modele giden icerik. Tool sonuclari da bu yondedir.</summary>
    Input = 0,

    /// <summary>Modelden gelen icerik.</summary>
    Output = 1,
}

/// <summary>Bir <see cref="IContentGuard"/> cagrisinin baglami.</summary>
/// <remarks>
/// Baglam <strong>tek bir metin parcasini</strong> tasir, bir mesaj listesini degil.
/// Mesajlar birlestirilip tek metin olarak verilseydi iki mesajin sinirinda olusan
/// sahte bir desen eslesirdi (ornek: bir mesaj <c>4539</c> ile bitip sonraki
/// <c>5787…</c> ile baslarsa gecerli olmayan bir kart numarasi "bulunurdu").
/// </remarks>
public sealed record ContentGuardContext
{
    /// <summary>Denetimin yonu.</summary>
    public required ContentGuardDirection Direction { get; init; }

    /// <summary>Denetlenecek metin.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Calistirma kimligi. Calistirma kapsami disindan cagrilirsa <see langword="null"/>.
    /// </summary>
    public Guid? RunId { get; init; }

    /// <summary>Calistirmanin kiracisi. Bilinmiyorsa <see langword="null"/>.</summary>
    public string? TenantId { get; init; }

    /// <summary>Calistirmayi yuruten agent'in adi. Bilinmiyorsa <see langword="null"/>.</summary>
    public string? AgentName { get; init; }

    /// <summary>Cagrilan modelin kimligi. Bilinmiyorsa <see langword="null"/>.</summary>
    public string? ModelId { get; init; }
}
