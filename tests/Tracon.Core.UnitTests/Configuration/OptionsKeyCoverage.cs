using System.Collections;
using System.Reflection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Proves that a binding test names every member of an options type, each with a
/// value that differs from the shipped default.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OptionsSectionCoverageTests"/> proves that a <c>{Type}BindingTests</c>
/// class exists and carries a test. It cannot prove what the class covers. A test
/// that lists keys by hand locks today's members, but a member added tomorrow to the
/// options type and to neither the binder nor the test leaves both silent — the
/// shape that produced <c>TraconImageOptions.Timeout</c> and the lane
/// collections. This helper closes that gap from the test side, the way
/// <see cref="TraconOptionsBindingCoverageTests"/> does for the <c>TraconOptions</c>
/// tree.
/// </para>
/// <para>
/// Two checks. (a) Every public settable scalar, every collection and every nested
/// options object of the type is named by at least one key under the section path
/// (nested objects recursively). (b) Every named member is bound to a value that
/// differs from a new instance's value: a test value equal to the default proves
/// nothing, because the test passes whether or not the key was bound. The explicit
/// assertions stay in each test: (b) proves the value changed, not that it reached
/// the right member.
/// </para>
/// <para>
/// Reflection is allowed here. The AOT rule binds the library, not its tests.
/// </para>
/// </remarks>
internal static class OptionsKeyCoverage
{
    /// <summary>Fails unless the keys name every member and every named member left its default.</summary>
    /// <typeparam name="TOptions">The options type the section binds to.</typeparam>
    /// <param name="values">The configuration the test bound.</param>
    /// <param name="sectionPath">The section path, for example <c>Tracon:Quotas</c>.</param>
    /// <param name="bound">The options instance the container produced from <paramref name="values"/>.</param>
    /// <param name="exempt">
    /// Top-level members another test of the same class covers. Each name must be a
    /// real member: a stale exemption fails too.
    /// </param>
    public static void ShouldCoverEveryMember<TOptions>(
        IReadOnlyDictionary<string, string?> values,
        string sectionPath,
        TOptions bound,
        params string[] exempt)
        where TOptions : class, new()
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrEmpty(sectionPath);
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(exempt);

        var failures = new List<string>();
        var exemptNames = new HashSet<string>(exempt, StringComparer.Ordinal);
        var memberNames = Members(typeof(TOptions)).Select(static member => member.Property.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var name in exemptNames.Where(name => !memberNames.Contains(name)))
        {
            failures.Add($"'{name}' is exempt, but {typeof(TOptions).Name} has no such member.");
        }

        Check(values.Keys.ToList(), sectionPath, bound, new TOptions(), exemptNames, typeof(TOptions).Name, failures);

        failures.ShouldBeEmpty(
            customMessage: $"The binding test does not cover every member of {typeof(TOptions).Name}.{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    private static void Check(
        IReadOnlyCollection<string> keys,
        string path,
        object bound,
        object shipped,
        HashSet<string> exempt,
        string display,
        List<string> failures)
    {
        foreach (var (property, kind) in Members(bound.GetType()))
        {
            if (exempt.Contains(property.Name))
            {
                continue;
            }

            var key = $"{path}:{property.Name}";
            var named = keys.Any(candidate =>
                string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));

            if (!named)
            {
                failures.Add($"{display}.{property.Name}: no key under '{key}'. Add one, or bind it if the binder misses it.");
                continue;
            }

            var boundValue = property.GetValue(bound);
            var shippedValue = property.GetValue(shipped);

            switch (kind)
            {
                case MemberKind.Nested when boundValue is not null && shippedValue is not null:
                    Check(keys, key, boundValue, shippedValue, [], $"{display}.{property.Name}", failures);
                    break;

                case MemberKind.Collection when SameContent(boundValue, shippedValue):
                    failures.Add($"{display}.{property.Name}: the bound collection equals the shipped default, so the test proves nothing.");
                    break;

                case MemberKind.Scalar when Equals(boundValue, shippedValue):
                    failures.Add($"{display}.{property.Name}: the test value '{boundValue}' equals the shipped default, so the test proves nothing.");
                    break;
            }
        }
    }

    private static IEnumerable<(PropertyInfo Property, MemberKind Kind)> Members(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true })
            {
                continue;
            }

            var settable = property.SetMethod is { IsPublic: true };
            var propertyType = property.PropertyType;

            if (propertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propertyType))
            {
                yield return (property, MemberKind.Collection);
            }
            else if (settable)
            {
                yield return (property, MemberKind.Scalar);
            }
            else if (propertyType.IsClass && propertyType.Namespace?.StartsWith("Tracon", StringComparison.Ordinal) == true)
            {
                yield return (property, MemberKind.Nested);
            }

            // A get-only scalar is computed, not configured: nothing to bind.
        }
    }

    private static bool SameContent(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is IDictionary leftMap && right is IDictionary rightMap)
        {
            return leftMap.Count == rightMap.Count
                && leftMap.Keys.Cast<object>().All(key => rightMap.Contains(key) && Equals(leftMap[key], rightMap[key]));
        }

        var leftItems = ((IEnumerable)left).Cast<object?>().ToList();
        var rightItems = ((IEnumerable)right).Cast<object?>().ToList();

        if (IsSet(left))
        {
            return leftItems.Count == rightItems.Count && leftItems.All(rightItems.Contains);
        }

        return leftItems.SequenceEqual(rightItems);
    }

    private static bool IsSet(object value)
        => value.GetType().GetInterfaces()
            .Any(static contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ISet<>));

    private enum MemberKind
    {
        Scalar,
        Collection,
        Nested,
    }
}
