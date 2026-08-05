using System.Globalization;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// <see cref="ModelBinding.ProviderSettings"/> sozlugunu okuyan ve dogrulayan
/// yardimcilar.
/// </summary>
/// <remarks>
/// <para>
/// Her saglayici paketi kendi anahtar onekini bilir (<c>anthropic</c>, <c>google</c>)
/// ve destekledigi anahtarlarin listesini tasir. Bu tip o listeyi uygular; boylece
/// dogrulama mantigi her yeni saglayici paketinde bastan yazilmaz.
/// </para>
/// <para>
/// <strong>Bilinmeyen anahtar sessizce yok sayilmaz.</strong> Yok sayilan bir ayar,
/// kullanicinin bekledigi davranisi almamasina ve sebebini gorememesine yol acar —
/// <see cref="ModelBinding.ReasoningEffort"/> icin verilen kararin (K-034) aynisi.
/// </para>
/// <para>
/// Anahtar karsilastirmasi buyuk/kucuk harfe duyarli degildir. Sozluk bir
/// <c>jsonb</c> sutunundan cozuldugunde varsayilan (sirali) karsilastirici ile
/// gelir; bu yuzden arama sozlugun kendi karsilastiricisina birakilmaz.
/// </para>
/// </remarks>
public static class ModelProviderSettings
{
    /// <summary>
    /// Baglantidaki tum saglayici ayarlarinin verilen onege ait ve destekleniyor
    /// oldugunu dogrular.
    /// </summary>
    /// <param name="binding">Denetlenecek model baglantisi.</param>
    /// <param name="providerPrefix">Saglayicinin anahtar oneki. Ornek: <c>anthropic</c>.</param>
    /// <param name="supportedKeys">
    /// Saglayicinin destekledigi tam anahtarlar (onek dahil). Ornek:
    /// <c>anthropic.promptCaching</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Parametrelerden biri <see langword="null"/> ise.</exception>
    /// <exception cref="ArgumentException"><paramref name="providerPrefix"/> bos ise.</exception>
    /// <exception cref="AgentPrismException">
    /// Sozlukte baska bir saglayiciya ait veya taninmayan bir anahtar varsa. Mesaj
    /// desteklenen anahtarlari listeler.
    /// </exception>
    public static void Validate(
        ModelBinding binding,
        string providerPrefix,
        IReadOnlyCollection<string> supportedKeys)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPrefix);
        ArgumentNullException.ThrowIfNull(supportedKeys);

        if (binding.ProviderSettings.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(supportedKeys, StringComparer.OrdinalIgnoreCase);
        var prefix = providerPrefix + ".";
        List<string>? foreignKeys = null;
        List<string>? unknownKeys = null;

        foreach (var key in binding.ProviderSettings.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                (foreignKeys ??= []).Add(key);
            }
            else if (!known.Contains(key))
            {
                (unknownKeys ??= []).Add(key);
            }
        }

        if (foreignKeys is null && unknownKeys is null)
        {
            return;
        }

        var supported = supportedKeys.Count == 0
            ? $"'{providerPrefix}' saglayicisi hicbir ek ayar desteklemiyor"
            : $"Desteklenen anahtarlar: {string.Join(", ", supportedKeys.Order(StringComparer.Ordinal))}";

        // Iki hata sinifi ayri ayri anlatilir: yanlis onek "ayari baska bir
        // saglayiciya yazdin" demektir, taninmayan anahtar ise yazim hatasi veya
        // desteklenmeyen bir ozelliktir. Ikisini tek mesajda birlestirmek
        // kullaniciyi yanlis yone yollar.
        var problems = new List<string>(2);

        if (foreignKeys is not null)
        {
            problems.Add(
                $"su anahtarlar '{providerPrefix}' saglayicisina ait degil: {string.Join(", ", foreignKeys)}. " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)} yalnizca " +
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.Provider)} alanindaki saglayicinin " +
                "anahtarlarini tasiyabilir; saglayici degistirildiginde eski ayarlar temizlenmelidir");
        }

        if (unknownKeys is not null)
        {
            problems.Add($"su anahtarlar taninmiyor: {string.Join(", ", unknownKeys)}");
        }

        throw new AgentPrismException(
            $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)} icinde " +
            $"{string.Join("; ayrica ", problems)}. {supported}.");
    }

    /// <summary>Bir ayari mantiksal deger olarak okur.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <param name="key">Tam anahtar. Ornek: <c>anthropic.promptCaching</c>.</param>
    /// <returns>Ayar tanimli degilse <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Deger mantiksal bir deger degilse.</exception>
    public static bool? ReadBoolean(ModelBinding binding, string key)
    {
        if (!TryGetValue(binding, key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,

            // Yapilandirma saglayicilari (appsettings.json disindaki ortam degiskeni
            // ve komut satiri) her degeri metin olarak tasir; "true" metnini
            // reddetmek kullaniciya sebebi anlasilmaz bir hata verirdi.
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw TypeMismatch(key, "mantiksal (true/false)", value),
        };
    }

    /// <summary>Bir ayari tam sayi olarak okur.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <param name="key">Tam anahtar. Ornek: <c>anthropic.thinking.budgetTokens</c>.</param>
    /// <returns>Ayar tanimli degilse <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Deger tam sayi degilse.</exception>
    public static int? ReadInt32(ModelBinding binding, string key)
    {
        if (!TryGetValue(binding, key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => throw TypeMismatch(key, "tam sayi", value),
        };
    }

    /// <summary>Bir ayari metin olarak okur.</summary>
    /// <param name="binding">Model baglantisi.</param>
    /// <param name="key">Tam anahtar. Ornek: <c>google.safety.harassment</c>.</param>
    /// <returns>Ayar tanimli degilse veya bos ise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Deger metin degilse.</exception>
    public static string? ReadString(ModelBinding binding, string key)
    {
        if (!TryGetValue(binding, key, out var value))
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.String)
        {
            throw TypeMismatch(key, "metin", value);
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool TryGetValue(ModelBinding binding, string key, out JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (binding.ProviderSettings.TryGetValue(key, out value))
        {
            return IsPresent(value);
        }

        foreach (var pair in binding.ProviderSettings)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return IsPresent(value);
            }
        }

        value = default;
        return false;
    }

    // Atanmamis bir JsonElement'in ValueKind degeri Undefined'dir; "yok" ile ayni
    // anlama gelir ve okuma yolunda istisnaya donusmemelidir.
    private static bool IsPresent(JsonElement value)
        => value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private static AgentPrismException TypeMismatch(string key, string expected, JsonElement value)
        => new($"'{key}' saglayici ayari {expected} bir deger bekliyor; gelen deger {value.ValueKind}.");
}
