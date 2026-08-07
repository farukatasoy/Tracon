using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// AgentPrism'in yerlesik desen tabanli icerik guard'i.
/// </summary>
/// <remarks>
/// <para>
/// Genisleme noktasi tek basina yeterli degildir: K-018 "bellek ici uygulama
/// birinci siniftir" der ve yerlesik bir uygulama olmadan <see cref="IContentGuard"/>
/// bos bir vaat olurdu. Uc desen ailesi tasir: yasak sozcuk listesi
/// (<see cref="ContentGuardAction.Block"/>), PII desenleri ve <c>secret</c>
/// desenleri (<see cref="ContentGuardAction.Mask"/>).
/// </para>
/// <para>
/// 🚨 <strong>Her desen kaynak ureteciyle yazilir</strong>
/// (<see cref="GeneratedRegexAttribute"/>). <c>AgentPrism.Core</c> AOT uyumludur;
/// calisma aninda derlenen bir <see cref="Regex"/> bunu bozar.
/// </para>
/// <para>
/// 🚨 <strong>Her desen zaman asimi tasir</strong> (1000 ms). ReDoS'a karsi tek
/// savunma budur ve sicak yolda zorunludur.
/// </para>
/// <para>
/// 🚨 Kart ve TC kimlik desenleri <see cref="CheckDigits"/> ile dogrulanir. Bu
/// olmadan her siparis numarasi maskelenir ve guard kapatilir.
/// </para>
/// <para>
/// Tahsis duzeni: once <c>IsMatch</c> / <c>EnumerateMatches</c> ile eslesme
/// aranir, yeni dize <strong>yalnizca eslesme varsa</strong> uretilir. Hicbir
/// kural tanimli degilse ilk satirda <see cref="ContentGuardResult.Allow"/>
/// donulur.
/// </para>
/// </remarks>
public sealed partial class PatternContentGuard : IContentGuard
{
    private readonly IOptionsMonitor<PatternContentGuardOptions> _options;

    /// <summary>Yeni bir yerlesik guard olusturur.</summary>
    /// <param name="options">Desen ve yasak sozcuk ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public PatternContentGuard(IOptionsMonitor<PatternContentGuardOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public string Name => "pattern";

    /// <inheritdoc />
    public ValueTask<ContentGuardResult> InspectAsync(
        ContentGuardContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.CurrentValue;

        // Hicbir kural tanimli degilse hicbir desen calismaz. Guard'i kayitli
        // birakip gecici olarak etkisizlestirmenin yolu budur.
        if (options.DeniedTerms.Count == 0 && options.MaskedPii == PiiPatterns.None)
        {
            return ValueTask.FromResult(ContentGuardResult.Allow);
        }

        var text = context.Text;

        foreach (var term in options.DeniedTerms)
        {
            if (!string.IsNullOrEmpty(term) &&
                text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                // 🚨 Sebep metni ne engellenen icerigi ne de yasak sozcugun
                // kendisini tasir: sozcuk listesi de kurumsal bir sirdir
                // ("gizli-proje" bir kod adi olabilir).
                return ValueTask.FromResult(ContentGuardResult.Block(
                    "denied-term",
                    "Icerik yapilandirilmis yasak sozcuk listesiyle eslesti."));
            }
        }

        return ValueTask.FromResult(Mask(text, options));
    }

    private static ContentGuardResult Mask(string text, PatternContentGuardOptions options)
    {
        var masked = text;
        List<string>? rules = null;

        if (options.MaskedPii.HasFlag(PiiPatterns.ProviderApiKey))
        {
            Apply(ApiKeyPattern(), "provider-api-key", CheckDigitKind.None, options, ref masked, ref rules);
        }

        if (options.MaskedPii.HasFlag(PiiPatterns.CreditCard))
        {
            Apply(CreditCardPattern(), "credit-card", CheckDigitKind.Luhn, options, ref masked, ref rules);
        }

        // TC kimlik denetimi karttan SONRA calisir: 16 haneli bir kart numarasinin
        // icinde 11 haneli gecerli bir kimlik dizisi bulunmasi mumkundur ve kart
        // once maskelenirse o sahte eslesme hic olusmaz.
        if (options.MaskedPii.HasFlag(PiiPatterns.TurkishNationalId))
        {
            Apply(TurkishNationalIdPattern(), "turkish-national-id", CheckDigitKind.TurkishNationalId, options, ref masked, ref rules);
        }

        if (options.MaskedPii.HasFlag(PiiPatterns.Iban))
        {
            Apply(IbanPattern(), "iban", CheckDigitKind.None, options, ref masked, ref rules);
        }

        if (options.MaskedPii.HasFlag(PiiPatterns.Email))
        {
            Apply(EmailPattern(), "email", CheckDigitKind.None, options, ref masked, ref rules);
        }

        return rules is null
            ? ContentGuardResult.Allow
            : ContentGuardResult.Mask(masked, string.Join(',', rules));
    }

    /// <summary>
    /// Bir deseni uygular. Eslesme yoksa <strong>hicbir dize uretilmez</strong>.
    /// </summary>
    /// <remarks>
    /// <paramref name="check"/> <see cref="CheckDigitKind.None"/> degilse eslesme
    /// bir kontrol basamagi denetiminden gecmek zorundadir; gecmeyen eslesme oldugu
    /// gibi birakilir. Bu yuzden once "gecerli bir eslesme var mi" sorusu tahsissiz
    /// olarak yanitlanir, <c>Replace</c> ancak ondan sonra cagrilir.
    /// </remarks>
    private static void Apply(
        Regex pattern,
        string ruleName,
        CheckDigitKind check,
        PatternContentGuardOptions options,
        ref string text,
        ref List<string>? rules)
    {
        if (!HasMatch(pattern, text, check))
        {
            return;
        }

        var replacement = options.MaskReplacement;

        text = check == CheckDigitKind.None
            ? pattern.Replace(text, replacement)
            : pattern.Replace(text, match => IsValid(check, match.ValueSpan) ? replacement : match.Value);

        (rules ??= []).Add(ruleName);
    }

    private static bool HasMatch(Regex pattern, string text, CheckDigitKind check)
    {
        if (check == CheckDigitKind.None)
        {
            return pattern.IsMatch(text);
        }

        // EnumerateMatches tahsis yapmaz: yalnizca konum ve uzunluk doner.
        foreach (var match in pattern.EnumerateMatches(text))
        {
            if (IsValid(check, text.AsSpan(match.Index, match.Length)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValid(CheckDigitKind check, ReadOnlySpan<char> value)
        => check == CheckDigitKind.TurkishNationalId
            ? CheckDigits.IsValidTurkishNationalId(value)
            : CheckDigits.IsValidLuhn(value);

    [GeneratedRegex(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(
        @"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex IbanPattern();

    // Iki bicim: kesintisiz 13-19 hane, veya dortlu gruplar. Ikisi de Luhn
    // denetiminden gecmek zorundadir; desen tek basina karar vermez.
    [GeneratedRegex(
        @"\b[0-9]{4}(?:[ \-][0-9]{4}){2,4}\b|\b[0-9]{13,19}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CreditCardPattern();

    [GeneratedRegex(
        @"\b[1-9][0-9]{10}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TurkishNationalIdPattern();

    [GeneratedRegex(
        @"\bsk-[A-Za-z0-9_\-]{16,}|\bghp_[A-Za-z0-9]{20,}|\bAKIA[0-9A-Z]{16}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ApiKeyPattern();
}
