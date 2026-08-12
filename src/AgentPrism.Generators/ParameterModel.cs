namespace AgentPrism.Generators;

/// <summary>Bir parametrenin baglama sekli.</summary>
internal enum ParameterShape
{
    /// <summary>Tek bir deger (JSON argumanindan cevrilir).</summary>
    Scalar,

    /// <summary>Bir dizi/liste (her eleman ayni yaprak tipe cevrilir).</summary>
    Array,

    /// <summary><see cref="System.Threading.CancellationToken"/> - semadan haric tutulur.</summary>
    CancellationToken,
}

/// <summary>
/// Bir parametrenin (veya dizi parametrenin elemaninin) yaprak CLR tipi.
/// JSON semasi ve donusturucu ifade bu bilgiden turetilir (52.5 - uretilen kod
/// yalnizca bu alanlarin bir fonksiyonu olmalidir; ayri bir "donusturucu metin"
/// alani TUTULMAZ, cunku iki temsil birbirinden sapabilir).
/// </summary>
internal sealed record LeafType(LeafTypeKind Kind, string ClrTypeDisplay, bool IsNullable, EquatableArray<string> EnumMemberNames)
{
    public static LeafType Scalar(LeafTypeKind kind, string clrTypeDisplay, bool isNullable)
        => new(kind, clrTypeDisplay, isNullable, EquatableArray<string>.Empty);

    public static LeafType Enum(string clrTypeDisplay, bool isNullable, EquatableArray<string> memberNames)
        => new(LeafTypeKind.Enum, clrTypeDisplay, isNullable, memberNames);
}

/// <summary>Desteklenen yaprak tip ailesi.</summary>
internal enum LeafTypeKind
{
    Boolean,
    Integer,
    Number,
    String,
    Guid,
    DateTime,
    DateTimeOffset,
    Enum,
}

/// <summary>Tek bir metot parametresinin uretec modeli.</summary>
/// <param name="IsConcreteArray">
/// <see cref="ParameterShape.Array"/> icin: parametrenin C# tipi cıplak bir dizi
/// (<c>T[]</c>) mi, yoksa <c>IReadOnlyList&lt;T&gt;</c> gibi bir arayuz mu.
/// Calisma zamani yardimcisi (<c>AgentPrismGeneratedToolArguments.GetArray</c>)
/// her zaman <c>IReadOnlyList&lt;T&gt;</c> doner; hedef <c>T[]</c> ise
/// <c>SourceWriter</c> bu alana bakarak bir <c>.ToArray()</c> donusumu ekler
/// (aksi halde CS1503).
/// </param>
internal sealed record ParameterModel(
    string Name,
    ParameterShape Shape,
    LeafType? Leaf,
    bool IsRequired,
    string? DefaultValueLiteral,
    bool IsConcreteArray = false);
