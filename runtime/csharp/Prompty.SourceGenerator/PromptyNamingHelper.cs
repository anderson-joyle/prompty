using System;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Prompty.SourceGenerator;

internal static class PromptyNamingHelper
{
    public static string DeriveClassName(string fileStem, string? frontmatterName)
    {
        if (!string.IsNullOrWhiteSpace(frontmatterName))
        {
            var candidate = frontmatterName!.Trim();
            if (!candidate.EndsWith("Prompty", StringComparison.Ordinal))
                candidate += "Prompty";
            if (SyntaxFacts.IsValidIdentifier(candidate))
                return candidate;
        }

        return ToPascalCase(fileStem) + "Prompty";
    }

    public static string DeriveNamespace(string rootNamespace, string relativeDir)
    {
        var ns = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(rootNamespace))
            ns.Append(SanitizeIdentifier(rootNamespace));

        if (!string.IsNullOrWhiteSpace(relativeDir))
        {
            var parts = relativeDir.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var sanitized = SanitizeSegment(part);
                if (string.IsNullOrEmpty(sanitized)) continue;
                if (ns.Length > 0) ns.Append('.');
                ns.Append(sanitized);
            }
        }

        return ns.Length > 0 ? ns.ToString() : "PromptyGenerated";
    }

    public static string ToParameterName(string inputName)
    {
        if (SyntaxFacts.GetKeywordKind(inputName) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(inputName) != SyntaxKind.None)
            return "@" + inputName;

        if (!SyntaxFacts.IsValidIdentifier(inputName))
            return "@" + SanitizeSegment(inputName);

        return inputName;
    }

    public static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Unnamed";
        var sb = new StringBuilder();
        bool nextUpper = true;
        foreach (char c in s)
        {
            if (c == '-' || c == '_' || c == ' ' || c == '.')
            {
                nextUpper = true;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(nextUpper ? char.ToUpperInvariant(c) : c);
                nextUpper = false;
            }
            else
            {
                nextUpper = true;
            }
        }
        if (sb.Length == 0) return "Unnamed";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment)) return string.Empty;
        var sb = new StringBuilder();
        foreach (char c in segment)
        {
            if (SyntaxFacts.IsIdentifierPartCharacter(c))
                sb.Append(c);
            else
                sb.Append('_');
        }
        if (sb.Length == 0) return "_";
        if (!SyntaxFacts.IsIdentifierStartCharacter(sb[0]))
            sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string SanitizeIdentifier(string ns)
    {
        // namespace may already contain dots — sanitize each segment
        var parts = ns.Split('.');
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (sb.Length > 0) sb.Append('.');
            sb.Append(SanitizeSegment(p));
        }
        return sb.ToString();
    }
}
