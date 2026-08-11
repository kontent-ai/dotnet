using System.Globalization;
// Shared test source, compiled into each test assembly - see src/testing/README.md.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kontent.Ai.Testing;

/// <summary>
/// Renders an assembly's exported surface as text, for an approval snapshot to pin. A diff here is a
/// change every consumer of that package can see.
/// </summary>
/// <remarks>
/// Output is ordered explicitly and ordinally throughout. Reflection promises no stable order for
/// interfaces or members, and the default string comparer is culture-sensitive - left to either, this
/// would produce snapshot diffs unrelated to any code change.
/// </remarks>
internal static class PublicApiApproval
{
    private const BindingFlags DeclaredPublic =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // Not thread-safe by contract, which is fine: one assembly is rendered at a time.
    private static readonly NullabilityInfoContext Nullability = new();

    internal static string Surface(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var builder = new StringBuilder();

        var exportedTypes = assembly.GetExportedTypes()
            .OrderBy(type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(type => type.Name, StringComparer.Ordinal);

        foreach (var type in exportedTypes)
        {
            builder.AppendLine($"// {type.Namespace}");
            builder.AppendLine(TypeSignature(type));

            foreach (var member in Members(type))
            {
                builder.AppendLine($"    {member}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string TypeSignature(Type type)
    {
        var modifiers = type.IsSealed && !type.IsValueType ? "sealed " : "";
        var generic = type.IsGenericType
            ? $"<{string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;

        return $"public {modifiers}{Kind(type)} {type.Name}{generic}{BaseTypes(type)}";
    }

    private static string Kind(Type type) => type switch
    {
        { IsInterface: true } => "interface",
        { IsEnum: true } => "enum",
        { IsValueType: true } => "struct",
        _ => "class",
    };

    private static string BaseTypes(Type type)
    {
        var bases = new List<string>();

        // object/ValueType/Enum are implied by the kind already rendered, so listing them is noise.
        if (type.BaseType is not null
            && type.BaseType != typeof(object)
            && type.BaseType != typeof(ValueType)
            && type.BaseType != typeof(Enum))
        {
            bases.Add(TypeName(type.BaseType));
        }

        // Interfaces are sorted rather than taken in reflection order, which is unspecified.
        bases.AddRange(type.GetInterfaces()
            .Where(candidate => !type.BaseType?.GetInterfaces().Contains(candidate) ?? true)
            .Select(candidate => TypeName(candidate))
            .OrderBy(name => name, StringComparer.Ordinal));

        return bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty;
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            // With the value: renumbering a member is a binary- and wire-breaking change that leaves the
            // names identical, so a name-only snapshot would not show it.
            var underlying = Enum.GetUnderlyingType(type);

            return Enum.GetNames(type)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name =>
                    $"{name} = {Convert.ChangeType(Enum.Parse(type, name), underlying, CultureInfo.InvariantCulture)}");
        }

        return Fields(type)
            .Concat(Constructors(type))
            .Concat(Properties(type))
            .Concat(Events(type))
            .Concat(Methods(type));
    }

    /// <summary>
    /// Constructors are public surface in their own right: adding one removes the implicit parameterless
    /// constructor, and changing a parameter breaks every caller.
    /// </summary>
    /// <remarks>
    /// Delegates are skipped - every delegate carries the same compiler-generated
    /// <c>(object, IntPtr)</c> constructor, so it can never differ; the signature that can is
    /// <c>Invoke</c>, already rendered as a method.
    /// </remarks>
    private static IEnumerable<string> Constructors(Type type) => typeof(Delegate).IsAssignableFrom(type)
        ? []
        : type.GetConstructors(DeclaredPublic)
            .Select(constructor => $".ctor({Parameters(constructor)})")
            .OrderBy(rendered => rendered, StringComparer.Ordinal);

    private static IEnumerable<string> Fields(Type type) => type
        .GetFields(DeclaredPublic)
        .Select(field => field.IsLiteral
            ? $"const {TypeName(field.FieldType)} {field.Name} = {field.GetRawConstantValue()}"
            : $"{RequiredModifier(field)}{FieldModifiers(field)}{TypeName(field.FieldType, Nullability.Create(field))} {field.Name}")
        .OrderBy(rendered => rendered, StringComparer.Ordinal);

    private static string FieldModifiers(FieldInfo field) => (field.IsStatic, field.IsInitOnly) switch
    {
        (true, true) => "static readonly ",
        (true, false) => "static ",
        (false, true) => "readonly ",
        (false, false) => "",
    };

    private static IEnumerable<string> Properties(Type type) => type
        .GetProperties(DeclaredPublic)
        .Select(property =>
        {
            var getter = property.GetMethod?.IsPublic == true ? "get; " : "";
            var setter = property.SetMethod switch
            {
                { IsPublic: true } method when IsInitOnly(method) => "init; ",
                { IsPublic: true } => "set; ",
                _ => "",
            };
            var isStatic = property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true;

            return $"{RequiredModifier(property)}{(isStatic ? "static " : "")}"
                 + $"{TypeName(property.PropertyType, Nullability.Create(property))} {property.Name} {{ {getter}{setter}}}";
        })
        .OrderBy(rendered => rendered, StringComparer.Ordinal);

    /// <summary>
    /// An init-only setter is a <c>set</c> accessor carrying the <c>IsExternalInit</c> modreq - the only
    /// place the distinction survives into metadata.
    /// </summary>
    private static bool IsInitOnly(MethodInfo setter) => setter.ReturnParameter
        .GetRequiredCustomModifiers()
        .Any(modifier => modifier.Name == "IsExternalInit");

    private static IEnumerable<string> Events(Type type) => type
        .GetEvents(DeclaredPublic)
        .Select(@event => $"event {TypeName(@event.EventHandlerType!, Nullability.Create(@event))} {@event.Name}")
        .OrderBy(rendered => rendered, StringComparer.Ordinal);

    private static IEnumerable<string> Methods(Type type) => type
        .GetMethods(DeclaredPublic)
        .Where(method => !method.IsSpecialName)
        .Select(method =>
        {
            var staticPrefix = method.IsStatic ? "static " : "";
            var generic = method.IsGenericMethod
                ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
                : string.Empty;
            var returnType = TypeName(method.ReturnType, Nullability.Create(method.ReturnParameter));
            return $"{staticPrefix}{returnType} {method.Name}{generic}({Parameters(method)})";
        })
        .OrderBy(rendered => rendered, StringComparer.Ordinal);

    private static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters()
        .Select(parameter =>
            $"{RefKind(parameter)}{TypeName(parameter.ParameterType, Nullability.Create(parameter))} {parameter.Name}"));

    /// <summary>
    /// How the argument is passed. Part of the signature a caller has to write, and the three kinds are
    /// otherwise indistinguishable in the snapshot.
    /// </summary>
    private static string RefKind(ParameterInfo parameter) => parameter.ParameterType.IsByRef
        ? parameter switch
        {
            { IsOut: true } => "out ",
            { IsIn: true } => "in ",
            _ => "ref ",
        }
        : string.Empty;

    /// <summary>
    /// A <c>required</c> member is part of the contract: dropping it silently stops obliging callers to
    /// set it, and adding one breaks every existing object initializer.
    /// </summary>
    private static string RequiredModifier(MemberInfo member) =>
        member.IsDefined(typeof(RequiredMemberAttribute), inherit: false) ? "required " : "";

    /// <summary>
    /// Renders a type, marking what may be null.
    /// </summary>
    /// <remarks>
    /// Nullability is metadata, not part of the type, so without <paramref name="nullability"/> a
    /// <c>string?</c> and a <c>string</c> are indistinguishable here - and narrowing one to the other is a
    /// breaking change this gate would otherwise wave through. <see cref="NullabilityInfoContext"/> decodes
    /// the compiler's annotations, including per-argument ones, so <c>IReadOnlyList&lt;string&gt;?</c> and
    /// <c>IReadOnlyList&lt;string?&gt;</c> read differently. <c>Nullable&lt;T&gt;</c> is rendered <c>T?</c>
    /// so value and reference nullability look the same.
    /// </remarks>
    private static string TypeName(Type type, NullabilityInfo? nullability = null)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return $"{TypeName(underlying)}?";
        }

        var rendered = type switch
        {
            // A byref type's own Name is the mangled "IEnumerable`1&", which loses the generic arguments -
            // so changing an out parameter's element type used to leave the snapshot untouched. The ref
            // kind itself is rendered with the parameter, where in/out/ref can be told apart.
            { IsByRef: true } => TypeName(type.GetElementType()!, nullability),
            { IsArray: true } => $"{TypeName(type.GetElementType()!, nullability?.ElementType)}[]",
            { IsGenericType: true } => RenderGeneric(type, nullability),
            _ => type.Name,
        };

        return nullability?.ReadState == NullabilityState.Nullable && CanBeAnnotated(type)
            ? $"{rendered}?"
            : rendered;
    }

    /// <summary>
    /// Whether a <c>?</c> on this type reflects the declaration rather than an inference about it.
    /// </summary>
    /// <remarks>
    /// An unconstrained type parameter is reported as nullable whatever the source said, because the
    /// caller decides: <c>T</c> may be instantiated with a nullable reference type. Rendering <c>T?</c>
    /// for a member declared <c>T</c> would misreport the contract, so only a parameter constrained to a
    /// reference type - where <c>T?</c> is something the author can actually write - carries the mark.
    /// </remarks>
    private static bool CanBeAnnotated(Type type) =>
        !type.IsGenericParameter
        || type.GenericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint);

    private static string RenderGeneric(Type type, NullabilityInfo? nullability)
    {
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        var arguments = type.GetGenericArguments();
        var argumentNullability = nullability?.GenericTypeArguments ?? [];

        var rendered = arguments.Select((argument, i) =>
            TypeName(argument, i < argumentNullability.Length ? argumentNullability[i] : null));

        return $"{name}<{string.Join(", ", rendered)}>";
    }
}
