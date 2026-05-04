using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Prompty.SourceGenerator;

internal static class PromptyCodeEmitter
{
    private const string GeneratorVersion = "1.0.0";

    public static string Emit(
        string namespaceName,
        string className,
        string rawContent,
        List<FrontmatterProperty>? inputs,
        List<FrontmatterProperty>? outputs)
    {
        var returnType = DeriveReturnType(outputs);
        var parameters = BuildParameterList(inputs);
        var dictInit = BuildDictInit(inputs);

        // CTRL-1: use SyntaxFactory.Literal to produce a correctly-escaped C# string literal
        var escapedContent = SyntaxFactory.Literal(rawContent).ToFullString();

        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine($"    [System.CodeDom.Compiler.GeneratedCode(\"Prompty.SourceGenerator\", \"{GeneratorVersion}\")]");
        sb.AppendLine($"    public static partial class {className}");
        sb.AppendLine("    {");
        sb.AppendLine($"        private const string _content = {escapedContent};");
        sb.AppendLine();
        sb.AppendLine("        private static readonly System.Lazy<global::Prompty.Core.Prompty> _agentLazy =");
        sb.AppendLine("            new System.Lazy<global::Prompty.Core.Prompty>(");
        sb.AppendLine("                () => global::Prompty.Core.PromptyLoader.LoadFromContent(_content));");
        sb.AppendLine();
        sb.AppendLine($"        public static async Task<{returnType}> RunAsync({parameters})");
        sb.AppendLine("        {");
        sb.AppendLine($"            var inputs = {dictInit};");
        sb.AppendLine($"            return ({returnType}) await global::Prompty.Core.Pipeline.InvokeAsync(_agentLazy.Value, inputs);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine($"        public static async Task<System.Collections.Generic.List<global::Prompty.Core.Message>> PrepareAsync({parameters})");
        sb.AppendLine("        {");
        sb.AppendLine($"            var inputs = {dictInit};");
        sb.AppendLine("            return await global::Prompty.Core.Pipeline.PrepareAsync(_agentLazy.Value, inputs);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string DeriveReturnType(List<FrontmatterProperty>? outputs)
    {
        if (outputs == null || outputs.Count == 0) return "string";
        if (outputs.Count > 1) return "object";
        return KindToCSharpType(outputs[0].Kind);
    }

    private static string BuildParameterList(List<FrontmatterProperty>? inputs)
    {
        if (inputs == null || inputs.Count == 0) return string.Empty;
        var parts = new List<string>();
        // C# requires optional parameters (with defaults) to follow required ones
        var ordered = inputs.OrderBy(p => p.Default != null ? 1 : 0);
        foreach (var prop in ordered)
        {
            var paramName = PromptyNamingHelper.ToParameterName(prop.Name ?? "param");
            var typeName = KindToCSharpType(prop.Kind);
            if (prop.Default != null)
            {
                var defaultLit = BuildDefaultLiteral(prop.Kind, prop.Default, typeName);
                parts.Add($"{typeName} {paramName} = {defaultLit}");
            }
            else
            {
                parts.Add($"{typeName} {paramName}");
            }
        }
        return string.Join(", ", parts);
    }

    private static string BuildDictInit(List<FrontmatterProperty>? inputs)
    {
        if (inputs == null || inputs.Count == 0)
            return "new System.Collections.Generic.Dictionary<string, object?>()";

        var entries = new List<string>();
        foreach (var prop in inputs)
        {
            var paramName = PromptyNamingHelper.ToParameterName(prop.Name ?? "param");
            var rawName = SyntaxFactory.Literal(prop.Name ?? "param").ToFullString();
            entries.Add($"        {{ {rawName}, {paramName} }}");
        }
        return "new System.Collections.Generic.Dictionary<string, object?>\r\n        {\r\n" +
               string.Join(",\r\n", entries) +
               "\r\n        }";
    }

    private static string BuildDefaultLiteral(string? kind, object defaultValue, string typeName)
    {
        var raw = defaultValue?.ToString() ?? "null";
        switch (kind?.ToLowerInvariant())
        {
            case "integer": return $"{raw}L";
            case "float": return $"{raw}d";
            case "boolean":
                return raw.ToLowerInvariant() == "true" ? "true" : "false";
            case "string":
                return SyntaxFactory.Literal(raw).ToFullString();
            default:
                return "default";
        }
    }

    internal static string KindToCSharpType(string? kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "string" => "string",
            "integer" => "long",
            "float" => "double",
            "boolean" => "bool",
            "array" => "System.Collections.Generic.IList<object>",
            "object" => "System.Collections.Generic.Dictionary<string, object?>",
            _ => "object"
        };
    }
}
