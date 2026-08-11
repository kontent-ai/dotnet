using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.ModelGenerator.Core.Configuration;

namespace Kontent.Ai.ModelGenerator.CommandLine;

/// <summary>
/// Argument helpers for the CLI tool. Supports both Delivery and Management modes; the latter
/// is opted into with the <c>-m</c> / <c>--management</c> flag.
/// </summary>
internal static class ArgHelpers
{
    private const char NameAndValueSeparator = '=';
    private const char NamePrefix = '-';

    private static readonly ProgramOptionsData<DeliveryOptions> DeliveryProgramOptionsData =
        new(typeof(DeliveryOptions), "delivery-sdk-net");

    private static readonly ProgramOptionsData<ManagementOptions> ManagementProgramOptionsData =
        new(typeof(ManagementOptions), "management-sdk-net");

    /// <summary>
    /// Generator options only one mode reads, so the generic <c>--&lt;PropertyName&gt;</c> form has to be
    /// mode-scoped as well. <see cref="CodeGeneratorOptions.Nullability"/> selects how Delivery read models
    /// express nullability; management models are uniformly nullable because <c>null</c> means "leave this
    /// element alone" on upsert, so there is nothing there for it to select.
    /// </summary>
    private static readonly IReadOnlySet<string> DeliveryOnlyGeneratorOptions =
        new HashSet<string>([nameof(CodeGeneratorOptions.Nullability)], StringComparer.OrdinalIgnoreCase);

    // The section-qualified form of every option, e.g. ManagementOptions:ApiKey. Mode-scoped for the same
    // reason the mapping tables are: only the running mode's section is ever read.
    private static readonly IReadOnlyList<string> DeliveryOptionKeys = SectionKeysOf(DeliveryProgramOptionsData);
    private static readonly IReadOnlyList<string> ManagementOptionKeys = SectionKeysOf(ManagementProgramOptionsData);

    /// <summary>
    /// Returns true if <c>-m</c> or <c>--management</c> appears anywhere in <paramref name="args"/>.
    /// </summary>
    public static bool IsManagementMode(string[] args) => args.Any(IsModeSwitch);

    /// <summary>
    /// Strips mode-switch flags (<c>-m</c> / <c>--management</c>) from <paramref name="args"/>
    /// so the remaining list can be fed to the configuration system (which would otherwise
    /// reject them since they don't bind to a config property).
    /// </summary>
    public static string[] StripModeSwitches(string[] args) =>
        args.Where(a => !IsModeSwitch(a)).ToArray();

    private static bool IsModeSwitch(string arg) =>
        string.Equals(arg, ArgMappingsRegister.ManagementShortFlag, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, ArgMappingsRegister.ManagementLongFlag, StringComparison.OrdinalIgnoreCase);

    public static IDictionary<string, string> GetSwitchMappings(string[] args) =>
        ArgMappingsRegister.GeneralMappings
            .Union(ArgMappingsRegister.ModeMappings(IsManagementMode(args)))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns every problem with the supplied arguments; an empty list means they are usable.
    /// </summary>
    /// <remarks>
    /// Reporting is the caller's job. Parsing runs before the container exists, so there is no logger to
    /// resolve here, and writing to <see cref="Console"/> directly is what previously forced the whole
    /// test assembly to run sequentially.
    /// </remarks>
    public static IReadOnlyList<string> FindInvalidArgs(string[] args)
    {
        var problems = new List<string>();
        var managementMode = IsManagementMode(args);
        var codeGeneratorOptionsProperties = typeof(CodeGeneratorOptions).GetProperties()
            .Where(p => p.PropertyType != DeliveryProgramOptionsData.Type
                        && p.PropertyType != ManagementProgramOptionsData.Type)
            .Select(p => p.Name)
            .Where(name => !managementMode || !DeliveryOnlyGeneratorOptions.Contains(name))
            .ToList();

        foreach (var arg in args.Where(StartsWithArgumentName))
        {
            var argumentName = SplitArgument(arg).FirstOrDefault() ?? string.Empty;

            if (IsKnownInMode(argumentName, managementMode) ||
                IsOptionPropertyValid(ModeOptionKeys(managementMode), argumentName) ||
                IsOptionPropertyValid(codeGeneratorOptionsProperties, argumentName))
            {
                continue;
            }

            problems.Add(WrongModeProblem(argumentName, managementMode) ?? $"Unsupported parameter: {arg}");
        }

        problems.AddRange(ValidateEnumArgValues(args));

        return problems;
    }

    private static bool IsKnownInMode(string argumentName, bool managementMode) =>
        IsModeSwitch(argumentName) ||
        ArgMappingsRegister.GeneralMappings.ContainsKey(argumentName) ||
        ArgMappingsRegister.ModeMappings(managementMode).ContainsKey(argumentName);

    private static IReadOnlyList<string> ModeOptionKeys(bool managementMode) =>
        managementMode ? ManagementOptionKeys : DeliveryOptionKeys;

    /// <summary>
    /// The message for an argument that is real but belongs to the mode that is not running, or
    /// <c>null</c> when the argument is not a mode question at all.
    /// </summary>
    /// <remarks>
    /// Binding drops such an argument, so without this it takes effect nowhere and says nothing: omitting
    /// <c>--management</c> generated a full set of Delivery models over the output directory and exited 0.
    /// </remarks>
    private static string? WrongModeProblem(string argumentName, bool managementMode)
    {
        if (managementMode)
        {
            if (IsOptionPropertyValid(DeliveryOnlyGeneratorOptions, argumentName))
            {
                return $"{argumentName} applies to Delivery models only. Management models are always " +
                       "nullable, because a null element is left untouched on upsert.";
            }

            return ArgMappingsRegister.DeliveryMappings.ContainsKey(argumentName) ||
                   IsOptionPropertyValid(DeliveryOptionKeys, argumentName)
                ? $"{argumentName} configures the Delivery API, which --management does not use."
                : null;
        }

        return ArgMappingsRegister.ManagementMappings.ContainsKey(argumentName) ||
               IsOptionPropertyValid(ManagementOptionKeys, argumentName)
            ? $"{argumentName} configures the Management API. Add --management (or -m) to generate from it."
            : null;
    }

    private static IReadOnlyList<string> SectionKeysOf<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
        (ProgramOptionsData<T> programOptionsData) =>
            [.. programOptionsData.OptionProperties.Select(prop => $"{programOptionsData.OptionsName}:{prop.Name}")];

    private static IEnumerable<string> ValidateEnumArgValues(string[] args)
    {
        var enumKeys = BuildEnumArgKeyMap();
        if (enumKeys.Count == 0)
        {
            yield break;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!StartsWithArgumentName(arg)) continue;

            string key;
            string? value;
            var inlineSeparator = arg.IndexOf(NameAndValueSeparator);
            if (inlineSeparator >= 0)
            {
                key = arg[..inlineSeparator];
                value = arg[(inlineSeparator + 1)..];
            }
            else
            {
                key = arg;
                if (i + 1 < args.Length && !StartsWithArgumentName(args[i + 1]))
                {
                    value = args[++i];
                }
                else
                {
                    value = null;
                }
            }

            if (!enumKeys.TryGetValue(key, out var enumType)) continue;

            if (string.IsNullOrEmpty(value) || !Enum.TryParse(enumType, value, ignoreCase: true, out _))
            {
                var allowed = string.Join(", ", Enum.GetNames(enumType).Select(n => n.ToLowerInvariant()));
                yield return $"Invalid value for {key}: '{value ?? string.Empty}'. Allowed values: {allowed}.";
            }
        }
    }

    private static Dictionary<string, Type> BuildEnumArgKeyMap()
    {
        var enumProperties = typeof(CodeGeneratorOptions).GetProperties()
            .Where(p => p.PropertyType.IsEnum)
            .ToDictionary(p => p.Name, p => p.PropertyType, StringComparer.Ordinal);

        var keys = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type) in enumProperties)
        {
            keys[$"--{name}"] = type;
        }
        foreach (var (cliKey, target) in ArgMappingsRegister.GeneralMappings)
        {
            if (enumProperties.TryGetValue(target, out var type))
            {
                keys[cliKey] = type;
            }
        }
        return keys;
    }

    public static UsedSdkInfo GetUsedSdkInfo(bool managementMode = false) =>
        managementMode ? ManagementProgramOptionsData.UsedSdkInfo : DeliveryProgramOptionsData.UsedSdkInfo;

    private static bool IsOptionPropertyValid(IEnumerable<string> optionProperties, string arg) =>
        optionProperties.Any(prop =>
            string.Equals(GetPrefixedMappingName(prop), arg, StringComparison.OrdinalIgnoreCase));

    private static string GetPrefixedMappingName(string mappingName) => $"--{mappingName}";

    private static string[] SplitArgument(string arg) => arg.Split(NameAndValueSeparator);

    private static bool StartsWithArgumentName(string arg) => arg.StartsWith(NamePrefix);

    private sealed class ProgramOptionsData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>
    {
        public IEnumerable<PropertyInfo> OptionProperties { get; }
        public string OptionsName { get; }
        public UsedSdkInfo UsedSdkInfo { get; set; }
        public Type Type { get; }

        public ProgramOptionsData(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
            Type type,
            string sdkName)
        {
            UsedSdkInfo = new UsedSdkInfo(type, sdkName);
            Type = type;
            OptionProperties = type.GetProperties();
            OptionsName = type.Name;
        }

    }
}
