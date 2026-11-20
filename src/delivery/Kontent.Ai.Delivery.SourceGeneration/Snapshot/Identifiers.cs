using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Kontent.Ai.Delivery.SourceGeneration.Snapshot;

internal static class Identifiers
{
    /// <summary>
    /// Turns a codename into a PascalCase identifier the way the model generator tool does
    /// (<c>TextHelpers.GetValidPascalCaseIdentifierName</c>), so a model keeps its name when a project moves from
    /// the tool to the snapshot. Anything but an ASCII letter or digit separates words, leading digits are dropped,
    /// and null means nothing usable is left.
    /// </summary>
    public static string? ToPascalCase(string codename)
    {
        var builder = new StringBuilder(codename.Length);
        var startOfWord = true;

        foreach (var c in codename)
        {
            var isAsciiLetter = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
            var isAsciiDigit = c is >= '0' and <= '9';

            if (!isAsciiLetter && !isAsciiDigit)
            {
                startOfWord = true;
                continue;
            }

            if (builder.Length == 0 && isAsciiDigit)
                continue;

            builder.Append(startOfWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            startOfWord = false;
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    public static bool IsValidNamespace(string value) =>
        value.Split('.').All(static part =>
            SyntaxFacts.IsValidIdentifier(part)
            && SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None
            && SyntaxFacts.GetContextualKeywordKind(part) != SyntaxKind.GlobalKeyword);
}
