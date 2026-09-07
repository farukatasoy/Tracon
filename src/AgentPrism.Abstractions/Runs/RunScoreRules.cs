namespace AgentPrism;

/// <summary>
/// The shared rules a <see cref="RunScore"/> must satisfy before it is stored.
/// </summary>
/// <remarks>
/// <para>
/// Public because <see cref="IRunScoreStore"/> is an extension point: a
/// consumer's own store must enforce the same invariants, and the shipped
/// store contract tests hold it to them. Writing the rule a second time by
/// hand would produce a second validation path that drifts.
/// </para>
/// <para>
/// The AgentPrism HTTP endpoint calls <see cref="IsValidName"/> and the
/// <see cref="RunScoreKind.Categorical"/> invariant itself so it can answer
/// <c>400</c> instead of letting the store throw.
/// </para>
/// </remarks>
public static class RunScoreRules
{
    /// <summary>The name written when a caller supplies none.</summary>
    /// <remarks>
    /// Also the name the migration gives every human score row written before
    /// scores had names.
    /// </remarks>
    public const string DefaultName = "overall";

    /// <summary>The greatest allowed <see cref="RunScore.Name"/> length.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The greatest allowed <see cref="RunScore.TextValue"/> length.</summary>
    public const int MaxTextValueLength = 256;

    /// <summary>The human-readable name rule, used in error messages.</summary>
    public const string NameDescription = "A run score name must match [A-Za-z0-9._-]{1,64}.";

    /// <summary>Reports whether <paramref name="name"/> is a legal score name.</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns><see langword="true"/> if the name matches <c>[A-Za-z0-9._-]{1,64}</c>.</returns>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws when <paramref name="score"/> breaks a stored invariant.</summary>
    /// <param name="score">The score about to be written.</param>
    /// <exception cref="ArgumentNullException"><paramref name="score"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The name breaks <see cref="NameDescription"/>, or the value shape does
    /// not match <see cref="RunScore.Kind"/>.
    /// </exception>
    /// <remarks>
    /// The value-shape invariant: a <see cref="RunScoreKind.Categorical"/>
    /// score carries <see cref="RunScore.TextValue"/> and no
    /// <see cref="RunScore.Value"/>; every other kind carries
    /// <see cref="RunScore.Value"/> and no <see cref="RunScore.TextValue"/>.
    /// A <see langword="null"/> <see cref="RunScore.Value"/> stays legal on the
    /// other kinds — it records that no measurement was made.
    /// </remarks>
    public static void Validate(RunScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (!IsValidName(score.Name))
        {
            throw new ArgumentException(NameDescription, nameof(score));
        }

        if (score.Kind == RunScoreKind.Categorical)
        {
            if (string.IsNullOrEmpty(score.TextValue))
            {
                throw new ArgumentException(
                    "A categorical score must carry a TextValue.",
                    nameof(score));
            }

            if (score.TextValue.Length > MaxTextValueLength)
            {
                throw new ArgumentException(
                    $"A categorical score's TextValue can be at most {MaxTextValueLength} characters.",
                    nameof(score));
            }

            if (score.Value is not null)
            {
                throw new ArgumentException(
                    "A categorical score carries no numeric Value.",
                    nameof(score));
            }

            return;
        }

        if (score.TextValue is not null)
        {
            throw new ArgumentException(
                "Only a categorical score carries a TextValue.",
                nameof(score));
        }
    }
}
