// Shared test source, compiled into each test assembly - see src/testing/README.md.

using System.Reflection;
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
            .Select(TypeName)
            .OrderBy(name => name, StringComparer.Ordinal));

        return bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty;
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            return Enum.GetNames(type).OrderBy(name => name, StringComparer.Ordinal);
        }

        return Fields(type).Concat(Properties(type)).Concat(Methods(type));
    }

    private static IEnumerable<string> Fields(Type type) => type
        .GetFields(DeclaredPublic)
        .Select(field => field.IsLiteral
            ? $"const {TypeName(field.FieldType)} {field.Name} = {field.GetRawConstantValue()}"
            : $"{FieldModifiers(field)}{TypeName(field.FieldType)} {field.Name}")
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
            var setter = property.SetMethod?.IsPublic == true ? "set; " : "";
            var init = property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers()
                .Any(modifier => modifier.Name == "IsExternalInit") == true ? "init; " : "";
            var isStatic = property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true;

            return $"{(isStatic ? "static " : "")}{TypeName(property.PropertyType)} {property.Name} {{ {getter}{setter}{init}}}";
        })
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
            var parameters = string.Join(", ", method.GetParameters()
                .Select(parameter => $"{TypeName(parameter.ParameterType)} {parameter.Name}"));

            return $"{staticPrefix}{TypeName(method.ReturnType)} {method.Name}{generic}({parameters})";
        })
        .OrderBy(rendered => rendered, StringComparer.Ordinal);

    private static string TypeName(Type type)
    {
        if (type.IsArray)
        {
            return $"{TypeName(type.GetElementType()!)}[]";
        }

        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
        }

        return type.Name;
    }
}
