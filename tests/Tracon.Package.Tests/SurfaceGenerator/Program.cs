using System.Reflection;
using System.Text;

// Reads the packed Tracon assemblies by reflection and writes one C# file that touches their public registration,
// builder and option surface the way a consumer would: every extension method of a Tracon assembly is called with
// arguments built from its signature, every public type with settable members is constructed with all of them set,
// and every public interface and abstract class is implemented or derived from.
//
// The file is compiled by a separate project that turns every strictness switch on. Nothing here is run: the code is
// written to be compiled, so the arguments are typed placeholders and every generated member is internal.
//
// Arguments: <output .cs> <report .txt> <comma-separated assembly names> <aot|full>
//   aot  - the packages that promise Native AOT: a member that is marked as needing unreferenced code or dynamic code is
//          skipped, because the compiler warning on it is the contract. Every other member must be silent.
//   full - the same, for packages that make no AOT promise.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("usage: <output.cs> <report.txt> <assemblies> <aot|full>");
            return 2;
        }

        var assemblies = args[2]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static name => Assembly.Load(new AssemblyName(name)))
            .ToArray();

        var writer = new SurfaceWriter(assemblies, aot: string.Equals(args[3], "aot", StringComparison.Ordinal));

        File.WriteAllText(args[0], writer.Generate(), new UTF8Encoding(false));
        File.WriteAllText(args[1], writer.Report(), new UTF8Encoding(false));

        Console.WriteLine(writer.Summary());

        return 0;
    }
}

internal sealed class SurfaceWriter(Assembly[] assemblies, bool aot)
{
    private readonly NullabilityInfoContext _nullability = new();
    private readonly List<string> _skipped = [];
    private readonly List<string> _exposures = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly StringBuilder _code = new();
    private readonly Dictionary<Type, string> _stubs = [];
    private readonly List<(string Name, string Body)> _stubBodies = [];
    private readonly List<string> _roots = [];
    private readonly HashSet<Type> _rendered = [];
    private int _next;

    private Nullness N(ParameterInfo parameter) => Nullness.From(_nullability.Create(parameter), parameter.ParameterType);

    private Nullness N(PropertyInfo property) => Nullness.From(_nullability.Create(property), property.PropertyType);

    private Nullness N(EventInfo eventInfo) => Nullness.From(_nullability.Create(eventInfo), eventInfo.EventHandlerType);

    public string Summary() => string.Join(", ", _counts.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"));

    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine(Summary());

        foreach (var line in _skipped.Concat(_exposures).Order(StringComparer.Ordinal))
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }

    public string Generate()
    {
        var types = assemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => type.FullName is not null && !IsCompilerGenerated(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToList();

        var members = new StringBuilder();

        // Stubs come first: a generic method that takes an implementation of an interface is closed over its stub.
        foreach (var type in types.Where(static type => type.IsInterface || (type.IsClass && type.IsAbstract && !type.IsSealed)))
        {
            _ = StubNameFor(type);
        }

        foreach (var type in types)
        {
            if (type.IsAbstract && type.IsSealed)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                             .Where(static method => method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), inherit: false))
                             .OrderBy(static method => method.ToString(), StringComparer.Ordinal))
                {
                    members.Append(TryEntryPoint(method));
                }

                continue;
            }

            if (type.IsInterface || (type.IsClass && type.IsAbstract))
            {
                continue;
            }

            members.Append(TryOptions(type));
        }

        _code.AppendLine("#nullable enable");
        _code.AppendLine("// The public surface of the packed packages, written by reflection (see the generator for the rules).");
        _code.AppendLine("namespace SurfaceProbe");
        _code.AppendLine("{");
        _code.AppendLine("    internal static class SurfaceTouch");
        _code.AppendLine("    {");
        _code.Append(members);
        _code.AppendLine("    }");
        _code.AppendLine();

        foreach (var (_, body) in _stubBodies)
        {
            _code.Append(body);
        }

        // The root: a consumer program that calls every one of the above is what a trimming publish needs in order to look at
        // them. A program that never calls a method is trimmed away before its warnings are asked for.
        _code.AppendLine("    internal static class SurfaceRoot");
        _code.AppendLine("    {");

        const int PerMethod = 25;
        var chunks = (_roots.Count + PerMethod - 1) / PerMethod;

        for (var chunk = 0; chunk < chunks; chunk++)
        {
            _code.AppendLine($"        private static void Part{chunk}()");
            _code.AppendLine("        {");

            foreach (var root in _roots.Skip(chunk * PerMethod).Take(PerMethod))
            {
                _code.AppendLine("            " + root);
            }

            _code.AppendLine("        }");
            _code.AppendLine();
        }

        _code.AppendLine("        internal static void Everything()");
        _code.AppendLine("        {");
        _code.AppendLine("            _ = global::SurfaceProbe.SurfaceInstances.Create();");

        for (var chunk = 0; chunk < chunks; chunk++)
        {
            _code.AppendLine($"            Part{chunk}();");
        }

        _code.AppendLine("        }");
        _code.AppendLine("    }");
        _code.AppendLine();
        // Every stub is built once: an internal class nothing creates is a warning of its own (CA1812).
        _code.AppendLine("    internal static class SurfaceInstances");
        _code.AppendLine("    {");
        _code.AppendLine("        internal static object[] Create() =>");
        _code.AppendLine("            new object[]");
        _code.AppendLine("            {");

        foreach (var (name, _) in _stubBodies)
        {
            _code.AppendLine($"                new {name}(),");
        }

        _code.AppendLine("            };");
        _code.AppendLine("    }");
        _code.AppendLine();

        _code.AppendLine("}");

        return _code.ToString();
    }

    private static bool IsCompilerGenerated(Type type) => type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);

    private static bool HasAttribute(MemberInfo member, string fullName)
        => member.CustomAttributes.Any(attribute => string.Equals(attribute.AttributeType.FullName, fullName, StringComparison.Ordinal));

    private static bool IsObsolete(MemberInfo member) => HasAttribute(member, "System.ObsoleteAttribute");

    private static bool NeedsAotContract(MemberInfo member)
        => HasAttribute(member, "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute")
            || HasAttribute(member, "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute")
            || HasAttribute(member, "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute");

    private static string? ExperimentalId(MemberInfo member)
    {
        var data = member.CustomAttributes.FirstOrDefault(static attribute => string.Equals(attribute.AttributeType.FullName, "System.Diagnostics.CodeAnalysis.ExperimentalAttribute", StringComparison.Ordinal));

        return data is null ? null : data.ConstructorArguments[0].Value as string;
    }

    /// <summary>The diagnostic ids of the experimental types rendered since the last <see cref="BeginMember"/>, and of the members themselves.</summary>
    private SortedSet<string> ExperimentalIds(IEnumerable<MemberInfo> members)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in _rendered)
        {
            for (var current = type; current is not null; current = current.DeclaringType)
            {
                if (ExperimentalId(current) is { } id)
                {
                    ids.Add(id);
                }
            }
        }

        foreach (var member in members)
        {
            if (ExperimentalId(member) is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private void BeginMember() => _rendered.Clear();

    /// <summary>
    /// A member whose signature names a type a dependency marks as experimental cannot be written without that diagnostic
    /// being raised: the consumer has to suppress it, and the guides say so. The generator suppresses it around that member
    /// alone and records the exposure, so a new one is a line in the report, not a silent change.
    /// </summary>
    private string Exposed(string what, string code, SortedSet<string> ids)
    {
        if (ids.Count == 0)
        {
            return code;
        }

        Count("experimental-exposed");
        _exposures.Add($"exposed {string.Join("+", ids)}: {what}");

        var list = string.Join(", ", ids);

        return $"#pragma warning disable {list}\n{code}#pragma warning restore {list}\n";
    }

    private void Count(string key) => _counts[key] = _counts.GetValueOrDefault(key) + 1;

    private void Skip(string kind, string what, string reason)
    {
        Count("skipped-" + kind);
        _skipped.Add($"skipped {kind}: {what}: {reason}");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Extension methods: the registration surface
    // ---------------------------------------------------------------------------------------------------------------

    private string TryEntryPoint(MethodInfo method)
    {
        var label = (method.DeclaringType!.FullName + "." + method.Name + "(" + string.Join(", ", method.GetParameters().Select(static parameter => parameter.ParameterType.Name)) + ")");

        if (IsObsolete(method) || IsObsolete(method.DeclaringType!))
        {
            Skip("entry", label, "obsolete: a call is a warning by contract");
            return string.Empty;
        }

        if (NeedsAotContract(method) || NeedsAotContract(method.DeclaringType!))
        {
            Count(aot ? "contract-entry" : "contract-entry-called");

            if (aot)
            {
                Skip("entry", label, "marked as needing unreferenced or dynamic code: the warning on a call is the contract");
                return string.Empty;
            }
        }

        BeginMember();

        try
        {
            var generic = BindGenericArguments(method);

            if (generic is null)
            {
                Skip("entry", label, "a generic argument has no type that satisfies its constraints");
                return string.Empty;
            }

            string? genericText = null;

            if (generic.Length != 0 && generic.Any(static argument => argument.Real is null))
            {
                // A stub stands in for a type argument: only a method whose signature does not mention the argument can be written.
                if (method.GetParameters().Any(static parameter => parameter.ParameterType.ContainsGenericParameters) || method.ReturnType.ContainsGenericParameters)
                {
                    Skip("entry", label, "a generic argument is a stub and the signature mentions it");
                    return string.Empty;
                }

                genericText = "<" + string.Join(", ", generic.Select(static argument => argument.Text)) + ">";
            }

            var parameters = method.GetParameters();
            var receiver = parameters[0];

            if (receiver.ParameterType.IsByRef || receiver.ParameterType.IsPointer || receiver.ParameterType.IsByRefLike)
            {
                Skip("entry", label, "receiver is by reference");
                return string.Empty;
            }

            var closed = generic.Length == 0 || genericText is not null ? method : method.MakeGenericMethod(generic.Select(static argument => argument.Real!).ToArray());
            var closedParameters = closed.GetParameters();
            var arguments = new List<string>();

            // The type comes from the closed method, the annotations from the open one: reflection reads a type argument of a
            // closed generic as nullable whatever the declaration says.
            for (var position = 1; position < closedParameters.Length; position++)
            {
                var parameter = closedParameters[position];

                if (parameter.IsOptional && !parameter.HasDefaultValue)
                {
                    break;
                }

                if (parameter.IsOptional)
                {
                    // Optional parameters are left to the compiler: a consumer who passes nothing gets the declared default.
                    break;
                }

                var value = Value(parameter.ParameterType, N(parameters[position]), depth: 0);

                if (value is null)
                {
                    Skip("entry", label, "no placeholder for parameter '" + parameter.Name + "' of type " + parameter.ParameterType.FullName);
                    return string.Empty;
                }

                arguments.Add(value);
            }

            var id = _next++;
            var receiverType = TypeName(closedParameters[0].ParameterType, N(parameters[0]));
            var call = StaticCall(closed, genericText, ["receiver", .. arguments]);
            var returnType = closed.ReturnType;
            var statement = new StringBuilder();

            if (returnType == typeof(void))
            {
                statement.Append("            ").Append(call).AppendLine(";");
            }
            else if (returnType.IsByRef || returnType.IsPointer || returnType.IsByRefLike)
            {
                Skip("entry", label, "returns a by-reference type");
                return string.Empty;
            }
            else
            {
                // A method that hands back the type it extends is a link of a fluent chain, and the next link is called on the
                // result: it goes into a local that cannot be null, so a nullable return is a warning here as it would be there.
                // Any other result goes into a local of its declared type.
                var fluent = returnType == closed.GetParameters()[0].ParameterType && !returnType.IsValueType;
                var resultType = fluent ? TypeName(returnType, null) : TypeName(returnType, N(method.ReturnParameter));

                statement.Append("            ").Append(resultType).Append(" result = ").Append(call).AppendLine(";");
                statement.AppendLine("            _ = result;");
            }

            Count(aot ? "entry-aot" : "entry-full");

            _roots.Add($"global::SurfaceProbe.SurfaceTouch.Entry{id}(default({receiverType})!);");

            return Exposed(label, $"        internal static void Entry{id}({receiverType} receiver)\n        {{\n{statement}        }}\n\n", ExperimentalIds([method, method.DeclaringType!]));
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            Skip("entry", label, exception.GetType().Name + ": " + exception.Message);
            return string.Empty;
        }
    }

    private string StaticCall(MethodInfo method, string? genericText, IReadOnlyList<string> arguments)
    {
        var name = TypeName(method.DeclaringType!, null) + "." + method.Name;

        if (genericText is not null)
        {
            name += genericText;
        }
        else if (method.IsGenericMethod)
        {
            name += "<" + string.Join(", ", method.GetGenericArguments().Select(argument => TypeName(argument, null))) + ">";
        }

        return name + "(" + string.Join(", ", arguments) + ")";
    }

    /// <summary>Picks a type for each generic parameter of a method, or <see langword="null"/> when none satisfies the constraints.</summary>
    private (string Text, Type? Real)[]? BindGenericArguments(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition)
        {
            return [];
        }

        var chosen = new List<(string, Type?)>();

        foreach (var parameter in method.GetGenericArguments())
        {
            var candidate = ChooseGenericArgument(parameter);

            if (candidate is null)
            {
                return null;
            }

            chosen.Add(candidate.Value);
        }

        return [.. chosen];
    }

    private (string Text, Type? Real)? ChooseGenericArgument(Type parameter)
    {
        var attributes = parameter.GenericParameterAttributes;
        var constraints = parameter.GetGenericParameterConstraints();

        if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
        {
            return constraints.Length == 1 && constraints[0] == typeof(ValueType) ? ("global::System.Int32", typeof(int)) : null;
        }

        Type[] plain = [typeof(object), typeof(string)];

        foreach (var candidate in plain)
        {
            if (constraints.All(constraint => !constraint.ContainsGenericParameters && constraint.IsAssignableFrom(candidate))
                && ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) == 0 || candidate == typeof(object)))
            {
                return (TypeName(candidate, null), candidate);
            }
        }

        // One interface constraint: the stub of that interface stands in.
        if (constraints.Length == 1 && constraints[0].IsInterface && !constraints[0].ContainsGenericParameters && _stubs.TryGetValue(constraints[0], out var stub))
        {
            return ("global::SurfaceProbe." + stub, null);
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Option and data types: every setter
    // ---------------------------------------------------------------------------------------------------------------

    private string TryOptions(Type type)
    {
        if (type.IsGenericTypeDefinition || type.IsAbstract || type.IsInterface || type.IsEnum || typeof(Delegate).IsAssignableFrom(type) || type.IsByRefLike)
        {
            return string.Empty;
        }

        var settable = SettableProperties(type).ToList();

        if (settable.Count == 0)
        {
            return string.Empty;
        }

        if (IsObsolete(type))
        {
            Skip("options", type.FullName!, "obsolete");
            return string.Empty;
        }

        BeginMember();

        var construction = Construction(type, depth: 0);

        if (construction is null)
        {
            Skip("options", type.FullName!, "no constructor the generator can call");
            return string.Empty;
        }

        var assignments = new List<string>();

        foreach (var property in settable)
        {
            if (IsObsolete(property))
            {
                continue;
            }

            var value = Value(property.PropertyType, N(property), depth: 1, forWrite: true);

            if (value is null)
            {
                Skip("option-member", type.FullName + "." + property.Name, "no placeholder for type " + property.PropertyType.FullName);
                continue;
            }

            assignments.Add($"                {property.Name} = {value},");
        }

        var id = _next++;
        var name = TypeName(type, null);

        Count("options-types");
        _counts["option-members"] = _counts.GetValueOrDefault("option-members") + assignments.Count;

        var initializer = assignments.Count == 0 ? string.Empty : "\n            {\n" + string.Join("\n", assignments) + "\n            }";

        _roots.Add($"_ = global::SurfaceProbe.SurfaceTouch.Options{id}();");

        return Exposed(type.FullName!, $"        internal static {name} Options{id}()\n        {{\n            return {construction}{initializer};\n        }}\n\n", ExperimentalIds([type, .. settable]));
    }

    private static IEnumerable<PropertyInfo> SettableProperties(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || property.SetMethod is null || !property.SetMethod.IsPublic)
            {
                continue;
            }

            if (!property.DeclaringType!.Assembly.GetName().Name!.StartsWith("Tracon", StringComparison.Ordinal))
            {
                continue; // inherited from a dependency: its diagnostics are the dependency's, not this package's
            }

            if (property.PropertyType.IsByRef || property.PropertyType.IsPointer || property.PropertyType.IsByRefLike)
            {
                continue;
            }

            yield return property;
        }
    }

    /// <summary>The expression that builds an instance, or <see langword="null"/>.</summary>
    private string? Construction(Type type, int depth)
    {
        if (type.IsValueType)
        {
            return "new " + TypeName(type, null) + "()";
        }

        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (constructors.Any(static constructor => constructor.GetParameters().Length == 0))
        {
            return "new " + TypeName(type, null) + "()";
        }

        // A type with only parameterized constructors (a positional record): the one with the most parameters.
        foreach (var constructor in constructors
                     .Where(static constructor => !IsCopyConstructor(constructor) && !IsObsolete(constructor))
                     .OrderByDescending(static constructor => constructor.GetParameters().Length))
        {
            var arguments = new List<string>();
            var ok = true;

            foreach (var parameter in constructor.GetParameters())
            {
                var value = Value(parameter.ParameterType, N(parameter), depth + 1);

                if (value is null || parameter.ParameterType.IsByRef)
                {
                    ok = false;
                    break;
                }

                arguments.Add(value);
            }

            if (ok)
            {
                return "new " + TypeName(type, null) + "(" + string.Join(", ", arguments) + ")";
            }
        }

        return null;
    }

    private static bool IsCopyConstructor(ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters();

        return parameters.Length == 1 && parameters[0].ParameterType == constructor.DeclaringType;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Interfaces and abstract classes: the extension seams
    // ---------------------------------------------------------------------------------------------------------------

    private string? StubNameFor(Type type)
    {
        if (_stubs.TryGetValue(type, out var existing))
        {
            return existing;
        }

        BeginMember();

        var body = BuildStub(type, out var reason);

        if (body is null)
        {
            Skip("stub", type.FullName!, reason ?? "unsupported");
            return null;
        }

        var name = "Stub" + _next++;
        _stubs[type] = name;
        _stubBodies.Add((name, Exposed(type.FullName!, body.Replace("%NAME%", name, StringComparison.Ordinal), ExperimentalIds([type, .. type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)]))));
        Count(type.IsInterface ? "stub-interface" : "stub-abstract-class");

        return name;
    }

    private string? BuildStub(Type type, out string? reason)
    {
        reason = null;

        if (type.IsGenericTypeDefinition)
        {
            reason = "generic type definition";
            return null;
        }

        if (IsObsolete(type))
        {
            reason = "obsolete";
            return null;
        }

        if (NeedsAotContract(type) && aot)
        {
            reason = "marked as needing unreferenced or dynamic code";
            return null;
        }

        var text = new StringBuilder();
        var typeName = TypeName(type, null);

        if (type.IsInterface)
        {
            text.AppendLine($"    internal sealed class %NAME% : {typeName}");
            text.AppendLine("    {");

            foreach (var iface in new[] { type }.Concat(type.GetInterfaces()))
            {
                if (iface.IsGenericTypeDefinition || iface.ContainsGenericParameters)
                {
                    reason = "inherits an open generic interface";
                    return null;
                }

                if (!AppendInterfaceMembers(text, iface, out reason))
                {
                    return null;
                }
            }

            text.AppendLine("    }");
            text.AppendLine();
            return text.ToString();
        }

        // An abstract class: derive, call the first constructor the generator can feed, override what is abstract.
        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(static candidate => candidate.IsPublic || candidate.IsFamily || candidate.IsFamilyOrAssembly)
            .OrderBy(static candidate => candidate.GetParameters().Length)
            .FirstOrDefault();

        if (constructor is null)
        {
            reason = "no accessible constructor";
            return null;
        }

        var baseArguments = new List<string>();

        foreach (var parameter in constructor.GetParameters())
        {
            var value = Value(parameter.ParameterType, N(parameter), depth: 1);

            if (value is null)
            {
                reason = "no placeholder for a base constructor parameter";
                return null;
            }

            baseArguments.Add(value);
        }

        text.AppendLine($"    internal sealed class %NAME% : {typeName}");
        text.AppendLine("    {");
        text.AppendLine($"        public %NAME%() : base({string.Join(", ", baseArguments)})");
        text.AppendLine("        {");
        text.AppendLine("        }");

        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (member is MethodInfo method && method.IsAbstract && !method.IsSpecialName)
            {
                if (!AppendOverride(text, method, out reason))
                {
                    return null;
                }
            }
            else if (member is PropertyInfo property
                     && (property.GetMethod?.IsAbstract == true || property.SetMethod?.IsAbstract == true))
            {
                if (!AppendPropertyOverride(text, property, out reason))
                {
                    return null;
                }
            }
            else if (member is EventInfo { AddMethod.IsAbstract: true })
            {
                reason = "abstract event";
                return null;
            }
        }

        text.AppendLine("    }");
        text.AppendLine();

        return text.ToString();
    }

    private bool AppendInterfaceMembers(StringBuilder text, Type iface, out string? reason)
    {
        reason = null;
        var ifaceName = TypeName(iface, null);

        foreach (var member in iface.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            switch (member)
            {
                case MethodInfo method when method.IsSpecialName:
                    continue;
                case MethodInfo { IsStatic: true } method when method.IsAbstract:
                    reason = "static abstract member " + method.Name;
                    return false;
                case MethodInfo { IsStatic: true }:
                    continue;
                case MethodInfo method when !method.IsAbstract:
                    continue; // a default implementation: nothing to write
                case MethodInfo method:
                    if (IsObsolete(method))
                    {
                        continue;
                    }

                    if (NeedsAotContract(method) && aot)
                    {
                        reason = "member " + method.Name + " is marked as needing unreferenced or dynamic code";
                        return false;
                    }

                    var signature = Signature(method, out reason);

                    if (signature is null)
                    {
                        return false;
                    }

                    text.AppendLine($"        {signature.Value.ReturnAttributes}{signature.Value.Return} {ifaceName}.{method.Name}{signature.Value.GenericParameters}({signature.Value.Parameters}) => throw new global::System.NotSupportedException();");
                    break;
                case PropertyInfo property:
                    if (IsObsolete(property))
                    {
                        continue;
                    }

                    if (property.GetIndexParameters().Length != 0)
                    {
                        reason = "indexer " + property.Name;
                        return false;
                    }

                    if (property.PropertyType.IsByRef || property.PropertyType.IsPointer)
                    {
                        reason = "by-reference property " + property.Name;
                        return false;
                    }

                    if (property.GetMethod?.IsAbstract != true && property.SetMethod?.IsAbstract != true)
                    {
                        continue; // both accessors have a default implementation
                    }

                    var propertyFlow = FlowAttributes(property.CustomAttributes);

                    text.Append($"        {(propertyFlow.Count == 0 ? string.Empty : "[" + string.Join(", ", propertyFlow) + "] ")}{TypeWithFlow(property.PropertyType, N(property), propertyFlow.Count != 0)} {ifaceName}.{property.Name} {{");

                    if (property.GetMethod?.IsAbstract == true)
                    {
                        text.Append(" get => throw new global::System.NotSupportedException();");
                    }

                    if (property.SetMethod?.IsAbstract == true)
                    {
                        text.Append(property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(static modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit")
                            ? " init { throw new global::System.NotSupportedException(); }"
                            : " set { throw new global::System.NotSupportedException(); }");
                    }

                    text.AppendLine(" }");
                    break;
                case EventInfo eventInfo:
                    if (IsObsolete(eventInfo))
                    {
                        continue;
                    }

                    text.AppendLine($"        event {TypeName(eventInfo.EventHandlerType!, N(eventInfo))} {ifaceName}.{eventInfo.Name} {{ add {{ }} remove {{ }} }}");
                    break;
            }
        }

        return true;
    }

    private bool AppendOverride(StringBuilder text, MethodInfo method, out string? reason)
    {
        reason = null;

        if (IsObsolete(method))
        {
            return true;
        }

        if (NeedsAotContract(method) && aot)
        {
            reason = "member " + method.Name + " is marked as needing unreferenced or dynamic code";
            return false;
        }

        var signature = Signature(method, out reason);

        if (signature is null)
        {
            return false;
        }

        var access = method.IsPublic ? "public" : "protected";

        text.AppendLine($"        {signature.Value.ReturnAttributes}{access} override {signature.Value.Return} {method.Name}{signature.Value.GenericParameters}({signature.Value.Parameters}) => throw new global::System.NotSupportedException();");

        return true;
    }

    private bool AppendPropertyOverride(StringBuilder text, PropertyInfo property, out string? reason)
    {
        reason = null;

        if (IsObsolete(property))
        {
            return true;
        }

        if (property.GetIndexParameters().Length != 0)
        {
            reason = "abstract indexer " + property.Name;
            return false;
        }

        var access = (property.GetMethod ?? property.SetMethod)!.IsPublic ? "public" : "protected";

        var overrideFlow = FlowAttributes(property.CustomAttributes);

        text.Append($"        {(overrideFlow.Count == 0 ? string.Empty : "[" + string.Join(", ", overrideFlow) + "] ")}{access} override {TypeWithFlow(property.PropertyType, N(property), overrideFlow.Count != 0)} {property.Name} {{");

        if (property.GetMethod?.IsAbstract == true)
        {
            text.Append(" get => throw new global::System.NotSupportedException();");
        }

        if (property.SetMethod?.IsAbstract == true)
        {
            text.Append(" set { throw new global::System.NotSupportedException(); }");
        }

        text.AppendLine(" }");

        return true;
    }

    private static readonly string[] FlowAttributeNames =
    [
        "System.Diagnostics.CodeAnalysis.AllowNullAttribute",
        "System.Diagnostics.CodeAnalysis.DisallowNullAttribute",
        "System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullAttribute",
        "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
    ];

    /// <summary>The nullable-flow attributes of a member, written the way source writes them.</summary>
    private static List<string> FlowAttributes(IEnumerable<CustomAttributeData> attributes)
    {
        var found = new List<string>();

        foreach (var data in attributes)
        {
            if (!FlowAttributeNames.Contains(data.AttributeType.FullName, StringComparer.Ordinal))
            {
                continue;
            }

            var name = "global::" + data.AttributeType.FullName!;
            var arguments = data.ConstructorArguments.Select(static argument => argument.Value switch
            {
                bool flag => flag ? "true" : "false",
                string text => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
                _ => "default",
            });

            found.Add(data.ConstructorArguments.Count == 0 ? name : name + "(" + string.Join(", ", arguments) + ")");
        }

        return found;
    }

    private string TypeWithFlow(Type type, Nullness info, bool hasFlow) => TypeName(type, info, suppressTopLevelNullable: hasFlow);

    private (string ReturnAttributes, string Return, string GenericParameters, string Parameters)? Signature(MethodInfo method, out string? reason)
    {
        reason = null;

        if (method.ReturnType.IsByRef || method.ReturnType.IsPointer || method.ReturnType.IsByRefLike)
        {
            reason = "member " + method.Name + " returns a by-reference type";
            return null;
        }

        var parameters = new List<string>();

        foreach (var parameter in method.GetParameters())
        {
            var type = parameter.ParameterType;

            if (type.IsPointer)
            {
                reason = "member " + method.Name + " has a pointer parameter";
                return null;
            }

            var modifier = string.Empty;

            if (type.IsByRef)
            {
                modifier = parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ";
                type = type.GetElementType()!;
            }

            var flow = FlowAttributes(parameter.CustomAttributes);
            var attributes = flow.Count == 0 ? string.Empty : "[" + string.Join(", ", flow) + "] ";

            parameters.Add(attributes + modifier + TypeWithFlow(type, N(parameter), flow.Count != 0) + " " + Identifier(parameter.Name ?? "arg" + parameter.Position));
        }

        var generics = method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(static argument => argument.Name)) + ">"
            : string.Empty;

        var returnFlow = FlowAttributes(method.ReturnParameter.CustomAttributes);
        var returnAttributes = returnFlow.Count == 0 ? string.Empty : "[return: " + string.Join(", ", returnFlow) + "] ";

        return (returnAttributes, TypeWithFlow(method.ReturnType, N(method.ReturnParameter), returnFlow.Count != 0), generics, string.Join(", ", parameters));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Placeholders: a typed value for every parameter or property
    // ---------------------------------------------------------------------------------------------------------------

    private string? Value(Type type, Nullness? nullability, int depth, bool forWrite = false)
    {
        if (type.IsByRef || type.IsPointer || type.IsByRefLike)
        {
            return null;
        }

        var nullable = nullability is not null && !type.IsValueType && (forWrite ? nullability.IsNullableWrite : nullability.IsNullable);
        var name = TypeName(type, null);

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return "(" + TypeName(type, null) + ")" + Value(underlying, null, depth)!;
        }

        if (type == typeof(string))
        {
            return nullable ? "(string?)null" : "\"value\"";
        }

        if (type == typeof(bool))
        {
            return "true";
        }

        if (type == typeof(char))
        {
            return "'x'";
        }

        if (type.IsPrimitive || type == typeof(decimal))
        {
            return "(" + name + ")1";
        }

        if (type.IsEnum)
        {
            var first = Enum.GetNames(type).FirstOrDefault();

            return first is null ? "default(" + name + ")" : name + "." + first;
        }

        if (type == typeof(TimeSpan))
        {
            return "global::System.TimeSpan.FromSeconds(1)";
        }

        if (type == typeof(Guid))
        {
            return "global::System.Guid.Empty";
        }

        if (type == typeof(Uri))
        {
            return nullable ? "(global::System.Uri?)null" : "new global::System.Uri(\"https://example.invalid/\")";
        }

        if (type == typeof(CancellationToken))
        {
            return "default(global::System.Threading.CancellationToken)";
        }

        if (type.IsValueType)
        {
            return "default(" + name + ")";
        }

        if (nullable)
        {
            return "(" + TypeName(type, nullability, suppressTopLevelNullable: true) + "?)null";
        }

        if (type.IsArray)
        {
            return type.GetArrayRank() == 1 ? "global::System.Array.Empty<" + TypeName(type.GetElementType()!, null) + ">()" : "default(" + name + ")!";
        }

        if (typeof(Delegate).IsAssignableFrom(type) && type.GetMethod("Invoke") is { } invoke)
        {
            return Lambda(type, invoke, depth, nullability);
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>) || definition == typeof(IEnumerable<>)
                || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>))
            {
                return "new global::System.Collections.Generic.List<" + TypeName(arguments[0], Argument(nullability, 0)) + ">()";
            }

            if (definition == typeof(HashSet<>) || definition == typeof(ISet<>))
            {
                return "new global::System.Collections.Generic.HashSet<" + TypeName(arguments[0], Argument(nullability, 0)) + ">()";
            }

            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                return "new global::System.Collections.Generic.Dictionary<" + TypeName(arguments[0], Argument(nullability, 0)) + ", " + TypeName(arguments[1], Argument(nullability, 1)) + ">()";
            }
        }

        // A type of this repository that can be built is built, so a nested option object is reached; anything else is a
        // typed null-forgiving default, which carries no nullable warning and needs no knowledge of the type.
        if (depth < 2 && !type.IsAbstract && !type.IsInterface && type.Assembly.GetName().Name!.StartsWith("Tracon", StringComparison.Ordinal)
            && type.GetConstructor(Type.EmptyTypes) is not null && !type.GetProperties().Any(static property => property.GetCustomAttributesData().Any(static data => data.AttributeType.Name == "RequiredMemberAttribute")))
        {
            return "new " + name + "()";
        }

        return "default(" + name + ")!";
    }

    private static Nullness? Argument(Nullness? nullability, int index)
        => nullability is not null && index < nullability.Arguments.Length ? nullability.Arguments[index] : null;

    private string Lambda(Type delegateType, MethodInfo invoke, int depth, Nullness? nullability)
    {
        var parameters = invoke.GetParameters();

        if (parameters.Any(static parameter => parameter.ParameterType.IsByRef) || invoke.ReturnType.IsByRef || invoke.ReturnType.IsByRefLike || parameters.Any(static parameter => parameter.ParameterType.IsByRefLike))
        {
            return "default(" + TypeName(delegateType, null) + ")!";
        }

        var returnType = invoke.ReturnType;
        var head = parameters.Length switch
        {
            0 => "static ()",
            1 => "static _",
            _ => "static (" + string.Join(", ", parameters.Select(static _ => "_")) + ")",
        };

        string body;

        if (returnType == typeof(void))
        {
            // An Action over an option type touches the settable members of that type: this is how a consumer configures it.
            body = parameters.Length == 1 && SettableForLambda(parameters[0].ParameterType) is { Count: > 0 } statements
                ? "{ " + string.Join(" ", statements.Select(statement => statement)) + " }"
                : "{ }";

            if (body != "{ }")
            {
                head = "static options";
                body = body.Replace("%P%", "options", StringComparison.Ordinal);
            }
        }
        else if (returnType == typeof(Task))
        {
            body = "global::System.Threading.Tasks.Task.CompletedTask";
        }
        else if (returnType == typeof(ValueTask))
        {
            body = "default(global::System.Threading.Tasks.ValueTask)";
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = returnType.GetGenericArguments()[0];
            body = "global::System.Threading.Tasks.Task.FromResult<" + TypeName(inner, null) + ">(" + DefaultOf(inner) + ")";
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var inner = returnType.GetGenericArguments()[0];
            body = "new global::System.Threading.Tasks.ValueTask<" + TypeName(inner, null) + ">(" + DefaultOf(inner) + ")";
        }
        else if (returnType == typeof(bool))
        {
            body = "false";
        }
        else
        {
            body = DefaultOf(returnType);
        }

        _ = depth;

        return "(" + TypeName(delegateType, nullability) + ")(" + head + " => " + body + ")";
    }

    private string DefaultOf(Type type) => type.IsValueType ? "default(" + TypeName(type, null) + ")" : "default(" + TypeName(type, null) + ")!";

    private List<string>? SettableForLambda(Type optionsType)
    {
        if (optionsType.IsInterface || optionsType.IsAbstract || optionsType.IsEnum || optionsType.IsPrimitive || optionsType == typeof(string)
            || !optionsType.Assembly.GetName().Name!.StartsWith("Tracon", StringComparison.Ordinal))
        {
            return null;
        }

        var statements = new List<string>();

        foreach (var property in SettableProperties(optionsType))
        {
            if (IsObsolete(property) || property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(static modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
            {
                continue;
            }

            var value = Value(property.PropertyType, N(property), depth: 1, forWrite: true);

            if (value is not null)
            {
                statements.Add($"%P%.{property.Name} = {value};");
            }
        }

        return statements;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Type names with nullable annotations
    // ---------------------------------------------------------------------------------------------------------------

    private string TypeName(Type type, Nullness? nullability, bool suppressTopLevelNullable = false)
    {
        _rendered.Add(type);

        if (type == typeof(void))
        {
            return "void";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsByRef)
        {
            return TypeName(type.GetElementType()!, nullability);
        }

        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            var element = TypeName(type.GetElementType()!, nullability?.Element);
            var array = element + "[" + new string(',', rank - 1) + "]";

            return !suppressTopLevelNullable && nullability?.IsNullable == true ? array + "?" : array;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return TypeName(underlying, null) + "?";
        }

        string text;

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var definition = type.GetGenericTypeDefinition();
            var baseName = (definition.FullName ?? definition.Name);
            var tick = baseName.IndexOf('`', StringComparison.Ordinal);
            baseName = (tick < 0 ? baseName : baseName[..tick]).Replace('+', '.');
            var rendered = new List<string>();

            for (var index = 0; index < arguments.Length; index++)
            {
                rendered.Add(TypeName(arguments[index], Argument(nullability, index)));
            }

            text = "global::" + baseName + "<" + string.Join(", ", rendered) + ">";
        }
        else
        {
            text = "global::" + (type.FullName ?? type.Name).Replace('+', '.');
        }

        return !suppressTopLevelNullable && !type.IsValueType && nullability?.IsNullable == true ? text + "?" : text;
    }

    private static string Identifier(string name) => IsKeyword(name) ? "@" + name : name;

    private static bool IsKeyword(string name) => name is "abstract" or "as" or "base" or "bool" or "break" or "byte" or "case" or "catch" or "char" or "checked" or "class" or "const" or "continue"
        or "decimal" or "default" or "delegate" or "do" or "double" or "else" or "enum" or "event" or "explicit" or "extern" or "false" or "finally" or "fixed" or "float" or "for" or "foreach"
        or "goto" or "if" or "implicit" or "in" or "int" or "interface" or "internal" or "is" or "lock" or "long" or "namespace" or "new" or "null" or "object" or "operator" or "out" or "override"
        or "params" or "private" or "protected" or "public" or "readonly" or "ref" or "return" or "sbyte" or "sealed" or "short" or "sizeof" or "stackalloc" or "static" or "string" or "struct"
        or "switch" or "this" or "throw" or "true" or "try" or "typeof" or "uint" or "ulong" or "unchecked" or "unsafe" or "ushort" or "using" or "virtual" or "void" or "volatile" or "while";
}

/// <summary>
/// The nullable annotations of a type as the compiler reads them. Reflection reports a type argument that is an unconstrained
/// type parameter as nullable; the declaration says nothing of the kind, and a consumer who closes the method over a
/// non-nullable type must not be given a nullable one.
/// </summary>
internal sealed class Nullness
{
    public bool IsNullable { get; init; }

    public bool IsNullableWrite { get; init; }

    public Nullness[] Arguments { get; init; } = [];

    public Nullness? Element { get; init; }

    /// <param name="info">What reflection reports for the member.</param>
    /// <param name="declared">The type as declared (open): where it is a type parameter, the annotation is not nullable.</param>
    public static Nullness From(NullabilityInfo info, Type? declared)
    {
        if (declared is { IsGenericParameter: true })
        {
            return new Nullness();
        }

        var declaredArguments = declared is { IsGenericType: true } ? declared.GetGenericArguments() : null;

        return new Nullness
        {
            IsNullable = info.ReadState == NullabilityState.Nullable,
            IsNullableWrite = info.WriteState == NullabilityState.Nullable,
            Arguments = [.. info.GenericTypeArguments.Select((argument, index) => From(argument, declaredArguments is not null && index < declaredArguments.Length ? declaredArguments[index] : null))],
            Element = info.ElementType is null ? null : From(info.ElementType, declared?.GetElementType()),
        };
    }
}
