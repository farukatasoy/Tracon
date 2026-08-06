namespace AgentPrism.Testing.Internal;

/// <summary>Bir model icin sirali yanit kuyrugu ve kuyruk tukendiginde kullanilacak varsayilan.</summary>
/// <remarks>
/// Kuyruk <strong>bir kez</strong> tuketilir: her cagri sirdaki bir sonraki adimi
/// coker. Kuyruk bostuktan sonra her cagri varsayilani dondurur — bir daha
/// asla sirdan okumaz. Bu, saglayicinin omru boyunca kalici, kestirilebilir bir
/// davranistir; mesaj gecmisini tarayip "hangi tool zaten cagrildi" cikarmaya
/// calisan gercek saglayici taklitlerinin (RoutingModelProvider,
/// ScriptedModelProvider) aksine, durum saglayicinin KENDISINDE tutulur.
/// </remarks>
internal sealed class FakeModelScript
{
    private readonly Queue<FakeStep> _queue = new();

    /// <summary>Kuyruk tukendiginde uretilecek metin. Varsayilan sabit bir yanittir.</summary>
    public string FallbackText { get; set; } = "fake response";

    /// <summary>Kuyruk tukendiginde uretilecek yanitin turu.</summary>
    public FakeFallbackKind Fallback { get; set; } = FakeFallbackKind.StaticText;

    /// <summary><see cref="FakeFallbackKind.EchoLastToolResult"/> icin metnin basina eklenecek onek.</summary>
    public string FallbackPrefix { get; set; } = string.Empty;

    /// <summary>Varsayilan yanitla birlikte bildirilecek token kullanimi. Cogu senaryoda bostur.</summary>
    public FakeUsage? FallbackUsage { get; set; }

    /// <summary>Sıraya bir adim ekler.</summary>
    public void Enqueue(FakeStep step) => _queue.Enqueue(step);

    /// <summary>Sıradaki adimi coker; kuyruk bossa <see langword="null"/>.</summary>
    public FakeStep? Dequeue() => _queue.TryDequeue(out var step) ? step : null;
}

/// <summary>Kuyruk tukendiginde uretilecek yanitin turu.</summary>
internal enum FakeFallbackKind
{
    /// <summary><see cref="FakeModelScript.FallbackText"/> aynen dondurulur.</summary>
    StaticText,

    /// <summary>Gelen son kullanici mesaji yankilanir.</summary>
    EchoUserMessage,

    /// <summary>Gecmisteki son tool sonucu (varsa <see cref="FakeModelScript.FallbackPrefix"/> ile) dondurulur.</summary>
    EchoLastToolResult,
}

/// <summary>Kuyruktaki tek bir adim: ya duz metin ya da bir tool cagrisi.</summary>
internal sealed record FakeStep
{
    public string? Text { get; init; }

    public string? ToolName { get; init; }

    public object? ToolArguments { get; init; }

    /// <summary>Bu adim doner donmez bildirilecek token kullanimi. Cogu adimda bostur.</summary>
    public FakeUsage? Usage { get; init; }
}

/// <summary>Bir adimla birlikte bildirilecek token kullanimi.</summary>
internal sealed record FakeUsage(int InputTokens, int OutputTokens);
