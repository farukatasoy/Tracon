namespace AgentPrism;

/// <summary>
/// Bir denetim izi dekoratorunun sardigi gercek depo uygulamasini disari acar.
/// </summary>
/// <remarks>
/// Denetim izi yazan depo dekoratorleri (<c>Auditing*Store</c>), tuketicinin
/// hangi depolama uygulamasini kullandigini bildiren kod yollarinda (ornek:
/// <c>{prefix}/api/meta</c>) saydam kalmalidir — arayuz "hangi depo kayitli"
/// sorusuna dekoratorun degil, gercek uygulamanin adiyla yanit almalidir.
/// </remarks>
public interface IAuditDecorated
{
    /// <summary>Dekoratorun sardigi gercek depo ornegi.</summary>
    object AuditedInner { get; }
}
