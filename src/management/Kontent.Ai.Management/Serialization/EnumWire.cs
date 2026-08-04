using System.Reflection;

namespace Kontent.Ai.Management.Serialization;

/// <summary>
/// Resolves an enum member's Management API wire token — its <see cref="JsonStringEnumMemberNameAttribute"/>
/// value, falling back to the member name.
/// </summary>
/// <remarks>
/// Serialization itself uses the built-in <c>JsonStringEnumConverter</c>, which reads the same attribute. This
/// exists for the places a token is needed with no serializer in play: PATCH path segments
/// (<c>/elements/{id}/allowed_blocks/{token}</c>) and polymorphic discriminator lookup. Resolution is kept
/// deliberately identical to the converter's — exact wire token, no case folding — so the two cannot disagree
/// about what a value is called on the wire.
/// </remarks>
internal static class EnumWire
{
    /// <summary>
    /// The wire token for <paramref name="value"/>. Throws for values not defined on the enum (a cast like
    /// <c>(TEnum)99</c>): this is a write API, and the value would otherwise ship as its numeric string.
    /// </summary>
    public static string ToValue<TEnum>(TEnum value) where TEnum : struct, Enum
        => Cache<TEnum>.ValueToName.TryGetValue(value, out var mapped)
            ? mapped
            : throw new ArgumentException($"'{value}' is not a defined {typeof(TEnum).Name} member.", nameof(value));

    /// <summary>Resolves a wire token back to its enum value, the inverse of <see cref="ToValue{TEnum}"/>. Returns <c>false</c> when no member matches.</summary>
    public static bool TryFromValue<TEnum>(string value, out TEnum result) where TEnum : struct, Enum
        => Cache<TEnum>.NameToValue.TryGetValue(value, out result);

    private static class Cache<TEnum> where TEnum : struct, Enum
    {
        internal static readonly Dictionary<TEnum, string> ValueToName = BuildValueToName();

        internal static readonly Dictionary<string, TEnum> NameToValue =
            ValueToName.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        private static Dictionary<TEnum, string> BuildValueToName()
        {
            var map = new Dictionary<TEnum, string>();
            foreach (var name in Enum.GetNames<TEnum>())
            {
                var wire = typeof(TEnum).GetField(name)!
                    .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? name;
                map[Enum.Parse<TEnum>(name)] = wire;
            }

            return map;
        }
    }
}
