using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Prompty.SourceGenerator;

internal sealed class PromptyFrontmatter
{
    public string? Name { get; set; }
    public List<FrontmatterProperty>? Inputs { get; set; }
    public List<FrontmatterProperty>? Outputs { get; set; }
}

internal sealed class FrontmatterProperty
{
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public bool? Required { get; set; }
    public object? Default { get; set; }
}

internal static class PromptyFrontmatterReader
{
    private static readonly Regex s_frontmatterRegex = new Regex(
        @"^---\s*\r?\n(?<yaml>.*?)\r?\n---",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly IDeserializer s_deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static PromptyFrontmatter? TryRead(string content)
    {
        try
        {
            var match = s_frontmatterRegex.Match(content);
            if (!match.Success)
                return new PromptyFrontmatter();

            var yaml = match.Groups["yaml"].Value;
            var raw = s_deserializer.Deserialize<Dictionary<string, object?>>(yaml);
            if (raw == null)
                return new PromptyFrontmatter();

            return ExtractFrontmatter(raw);
        }
        catch
        {
            return null;
        }
    }

    private static PromptyFrontmatter ExtractFrontmatter(Dictionary<string, object?> raw)
    {
        var result = new PromptyFrontmatter();

        if (raw.TryGetValue("name", out var nameObj) && nameObj is string name)
            result.Name = name;

        if (raw.TryGetValue("inputs", out var inputsObj))
            result.Inputs = ExtractProperties(inputsObj);

        if (raw.TryGetValue("outputs", out var outputsObj))
            result.Outputs = ExtractProperties(outputsObj);

        return result;
    }

    private static List<FrontmatterProperty>? ExtractProperties(object? obj)
    {
        if (obj == null) return null;

        var props = new List<FrontmatterProperty>();

        // YamlDotNet deserializes mappings as Dictionary<object,object> when target is object
        var dict = obj as Dictionary<object, object>
            ?? (obj is Dictionary<string, object?> sd ? ConvertDict(sd) : null);

        if (dict == null) return null;

        foreach (var kvp in dict)
        {
            var propName = kvp.Key?.ToString();
            if (propName == null) continue;

            var prop = new FrontmatterProperty { Name = propName };

            var inner = kvp.Value as Dictionary<object, object>
                ?? (kvp.Value is Dictionary<string, object?> sd2 ? ConvertDict(sd2) : null);

            if (inner != null)
            {
                if (inner.TryGetValue("type", out var typeVal))
                    prop.Kind = typeVal?.ToString();
                if (inner.TryGetValue("kind", out var kindVal))
                    prop.Kind = kindVal?.ToString();
                if (inner.TryGetValue("required", out var reqVal))
                    prop.Required = reqVal is bool b ? b : bool.TryParse(reqVal?.ToString(), out var bp) ? bp : (bool?)null;
                if (inner.TryGetValue("default", out var defVal))
                    prop.Default = defVal;
            }

            props.Add(prop);
        }

        return props.Count > 0 ? props : null;
    }

    private static Dictionary<object, object> ConvertDict(Dictionary<string, object?> source)
    {
        var result = new Dictionary<object, object>();
        foreach (var kvp in source)
            if (kvp.Value != null)
                result[kvp.Key] = kvp.Value;
        return result;
    }
}
