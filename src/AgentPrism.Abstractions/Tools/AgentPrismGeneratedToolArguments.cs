using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Kaynak ureteci (Faz 52) tarafindan uretilen tool sarmalayicilarinin JSON
/// argumanlarini CLR tiplerine cevirirken cagirdigi yardimci metotlar.
/// </summary>
/// <remarks>
/// <para>
/// Bu sinif yalnizca <c>[AgentPrismTool]</c> ureteci tarafindan uretilen kod
/// tarafindan cagirilmak uzere tasarlanmistir; elle cagirmak desteklenen bir
/// senaryo degildir. Public olmasinin tek sebebi, uretilen kodun tuketicinin
/// KENDI derlemesinde olusmasi ve bu yuzden bu tipe derleme disindan
/// erisebilmesi gerekmesidir.
/// </para>
/// <para>
/// Tum donusumler <strong>yansima kullanmaz</strong>: <see cref="JsonElement"/>
/// uzerindeki tipe-ozgu <c>Get*</c> metotlariyla veya cagiranin verdigi
/// donusturucu delege ile calisir.
/// </para>
/// </remarks>
public static class AgentPrismGeneratedToolArguments
{
    /// <summary>Zorunlu bir argumani okur; eksikse hata fırlatır.</summary>
    /// <typeparam name="T">Hedef CLR tipi.</typeparam>
    /// <param name="arguments">Cagrinin argumanlari.</param>
    /// <param name="name">Argumanin JSON anahtari.</param>
    /// <param name="convert">Bir <see cref="JsonElement"/>'i <typeparamref name="T"/>'ye ceviren donusturucu.</param>
    /// <returns>Cevrilen deger.</returns>
    /// <exception cref="AgentPrismException">Arguman eksikse veya beklenmeyen bir CLR tipindeyse.</exception>
    public static T GetRequired<T>(AIFunctionArguments arguments, string name, Func<JsonElement, T> convert)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(convert);

        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            throw new AgentPrismException($"'{name}' argumani eksik.");
        }

        return Convert(raw, convert, name);
    }

    /// <summary>Istege bagli bir argumani okur; eksikse verilen varsayilani doner.</summary>
    /// <typeparam name="T">Hedef CLR tipi.</typeparam>
    /// <param name="arguments">Cagrinin argumanlari.</param>
    /// <param name="name">Argumanin JSON anahtari.</param>
    /// <param name="convert">Bir <see cref="JsonElement"/>'i <typeparamref name="T"/>'ye ceviren donusturucu.</param>
    /// <param name="defaultValue">Arguman eksikse kullanilacak deger.</param>
    /// <returns>Cevrilen deger veya <paramref name="defaultValue"/>.</returns>
    /// <exception cref="AgentPrismException">Arguman verilmis ama beklenmeyen bir CLR tipindeyse.</exception>
    public static T GetOptional<T>(AIFunctionArguments arguments, string name, Func<JsonElement, T> convert, T defaultValue)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(convert);

        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            return defaultValue;
        }

        return Convert(raw, convert, name);
    }

    /// <summary>Bir dizi argumani okur; her eleman ayni donusturucuyle cevrilir.</summary>
    /// <typeparam name="T">Dizi eleman tipi.</typeparam>
    /// <param name="arguments">Cagrinin argumanlari.</param>
    /// <param name="name">Argumanin JSON anahtari.</param>
    /// <param name="convert">Her elemani ceviren donusturucu.</param>
    /// <param name="required">Arguman eksikse hata firlatilip firlatilmayacagi.</param>
    /// <param name="defaultValue">
    /// <paramref name="required"/> <see langword="false"/> ve arguman eksikse kullanilacak
    /// deger. <see langword="null"/> ise bos liste kullanilir.
    /// </param>
    /// <returns>Cevrilen dizi.</returns>
    /// <exception cref="AgentPrismException">
    /// Arguman zorunlu ama eksikse, veya verilen deger bir JSON dizisi degilse.
    /// </exception>
    public static IReadOnlyList<T> GetArray<T>(
        AIFunctionArguments arguments,
        string name,
        Func<JsonElement, T> convert,
        bool required,
        IReadOnlyList<T>? defaultValue)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(convert);

        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            if (required)
            {
                throw new AgentPrismException($"'{name}' argumani eksik.");
            }

            return defaultValue ?? [];
        }

        if (raw is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            var list = new List<T>(element.GetArrayLength());

            foreach (var item in element.EnumerateArray())
            {
                list.Add(convert(item));
            }

            return list;
        }

        throw new AgentPrismException($"'{name}' argumani bir JSON dizisi olmali, gelen tip: {raw.GetType()}.");
    }

    private static T Convert<T>(object raw, Func<JsonElement, T> convert, string name)
    {
        if (raw is JsonElement element)
        {
            return convert(element);
        }

        if (raw is T typed)
        {
            return typed;
        }

        throw new AgentPrismException($"'{name}' argumani beklenmeyen bir CLR tipinde: {raw.GetType()}.");
    }
}
