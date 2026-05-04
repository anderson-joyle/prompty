using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using System.Text;

namespace Prompty.SourceGenerator.Tests;

public class PromptySourceGeneratorTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static GeneratorRunResult RunGenerator(
        string filePath,
        string fileContent,
        string? rootNamespace = "TestNs",
        string? relativeDir = null)
    {
        var compilation = CreateBaseCompilation();
        var additionalText = new InMemoryAdditionalText(filePath, fileContent);

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            rootNamespace,
            relativeDir ?? string.Empty,
            filePath);

        var generator = new PromptySourceGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(additionalText))
            .WithUpdatedAnalyzerConfigOptions(optionsProvider);

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        return driver.GetRunResult().Results[0];
    }

    private static CSharpCompilation CreateBaseCompilation()
    {
        return CSharpCompilation.Create(
            "TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static string GetGeneratedSource(GeneratorRunResult result)
    {
        Assert.Single(result.GeneratedSources);
        return result.GeneratedSources[0].SourceText.ToString();
    }

    // -----------------------------------------------------------------------
    // TS-1 / TS-2: Generator runs and produces a source file
    // -----------------------------------------------------------------------

    [Fact]
    public void TS1_TS2_Generator_ProducesOneSourceFile_ForPromptyAdditionalText()
    {
        var result = RunGenerator("/project/chat.prompty", Fixtures.Chat);
        Assert.Single(result.GeneratedSources);
        Assert.EndsWith("ChatPrompty.g.cs", result.GeneratedSources[0].HintName, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // TS-3: Class name derived from filename stem
    // -----------------------------------------------------------------------

    [Fact]
    public void TS3_ChatPrompty_ClassNameDerivedFromFileStem()
    {
        var src = GetGeneratedSource(RunGenerator("/p/chat.prompty", Fixtures.Chat));
        Assert.Contains("public static partial class ChatPrompty", src);
    }

    [Fact]
    public void TS3_MyAgentPrompty_HyphenatedStemPascalCased()
    {
        var src = GetGeneratedSource(RunGenerator("/p/my-agent.prompty", Fixtures.MyAgent));
        Assert.Contains("public static partial class MyAgentPrompty", src);
    }

    // -----------------------------------------------------------------------
    // TS-4: Frontmatter name field overrides filename
    // -----------------------------------------------------------------------

    [Fact]
    public void TS4_FrontmatterName_OverridesFilenameWhenValidIdentifier()
    {
        var src = GetGeneratedSource(RunGenerator("/p/greetingbot.prompty", Fixtures.GreetingBot));
        Assert.Contains("public static partial class GreetingBotPrompty", src);
    }

    // -----------------------------------------------------------------------
    // TS-5: _content field present, no File I/O
    // -----------------------------------------------------------------------

    [Fact]
    public void TS5_ContentFieldPresent_NoFileIO()
    {
        var src = GetGeneratedSource(RunGenerator("/p/chat.prompty", Fixtures.Chat));
        Assert.Contains("private const string _content", src);
        Assert.DoesNotContain("File.ReadAllText", src);
        Assert.DoesNotContain("File.OpenRead", src);
    }

    // -----------------------------------------------------------------------
    // TS-6: Lazy-initialized agent via PromptyLoader.LoadFromContent
    // -----------------------------------------------------------------------

    [Fact]
    public void TS6_LazyAgent_LoadFromContent()
    {
        var src = GetGeneratedSource(RunGenerator("/p/chat.prompty", Fixtures.Chat));
        Assert.Contains("PromptyLoader.LoadFromContent(_content)", src);
        Assert.Contains("Lazy<", src);
    }

    // -----------------------------------------------------------------------
    // TS-7: Type mappings and default parameters
    // -----------------------------------------------------------------------

    [Fact]
    public void TS7_TypeMappings_StringAndIntegerWithDefault()
    {
        var src = GetGeneratedSource(RunGenerator("/p/greetingbot.prompty", Fixtures.GreetingBot));
        Assert.Contains("string firstName", src);
        Assert.Contains("long age = 30L", src);
    }

    [Fact]
    public void TS7_TypeMappings_AllKinds()
    {
        var src = GetGeneratedSource(RunGenerator("/p/typecheck.prompty", Fixtures.TypeCheck));
        Assert.Contains("bool flag", src);
        Assert.Contains("double score", src);
        Assert.Contains("System.Collections.Generic.IList<object> tags", src);
        Assert.Contains("System.Collections.Generic.Dictionary<string, object?> meta", src);
        Assert.Contains("object unknown_field", src);
    }

    // -----------------------------------------------------------------------
    // TS-8: Return type depends on outputs schema
    // -----------------------------------------------------------------------

    [Fact]
    public void TS8_NoOutputs_ReturnsTaskString()
    {
        const string noOutput = "---\ndescription: no out\ninputs:\n  q:\n    kind: string\n---\ntest";
        var src = GetGeneratedSource(RunGenerator("/p/noout.prompty", noOutput));
        Assert.Contains("Task<string> RunAsync", src);
    }

    [Fact]
    public void TS8_SingleBoolOutput_ReturnsTaskBool()
    {
        var src = GetGeneratedSource(RunGenerator("/p/typecheck.prompty", Fixtures.TypeCheck));
        Assert.Contains("Task<bool> RunAsync", src);
    }

    [Fact]
    public void TS8_MultipleOutputs_ReturnsTaskObject()
    {
        var src = GetGeneratedSource(RunGenerator("/p/multiout.prompty", Fixtures.MultiOut));
        Assert.Contains("Task<object> RunAsync", src);
    }

    // -----------------------------------------------------------------------
    // TS-9: PrepareAsync same parameters, returns List<Message>
    // -----------------------------------------------------------------------

    [Fact]
    public void TS9_PrepareAsync_SameParamsAndCorrectReturnType()
    {
        var src = GetGeneratedSource(RunGenerator("/p/greetingbot.prompty", Fixtures.GreetingBot));
        Assert.Contains("Task<System.Collections.Generic.List<global::Prompty.Core.Message>> PrepareAsync", src);
        // Both methods share parameters
        Assert.Contains("string firstName", src);
        Assert.Contains("long age = 30L", src);
    }

    // -----------------------------------------------------------------------
    // TS-10: Invalid YAML → zero sources, zero errors
    // -----------------------------------------------------------------------

    [Fact]
    public void TS10_InvalidYaml_ProducesNoSourcesAndNoErrors()
    {
        const string badYaml = "---\nkey: {unclosed: [bracket\n---\nbody";
        var result = RunGenerator("/p/bad.prompty", badYaml);
        Assert.Empty(result.GeneratedSources);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    // -----------------------------------------------------------------------
    // TS-11: CTRL-1 — string literal injection prevention
    // -----------------------------------------------------------------------

    [Fact]
    public void TS11_StringLiteralInjection_ContentWithSpecialChars_CompilesCleanly()
    {
        // content with quotes, backslashes, newlines — CTRL-1 must handle all of these
        const string dangerousContent = "---\ndescription: security\n---\nLine1\nHe said \"hello\\world\"\nEnd";
        var src = GetGeneratedSource(RunGenerator("/p/sec.prompty", dangerousContent));
        Assert.Contains("private const string _content", src);
        // The generated source must be syntactically valid C# — verify no raw unescaped quote breaks it
        var tree = CSharpSyntaxTree.ParseText(src);
        var diags = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(diags);
    }

    // -----------------------------------------------------------------------
    // TS-12: CTRL-2 — invalid frontmatter name falls back to filename
    // -----------------------------------------------------------------------

    [Fact]
    public void TS12_InvalidFrontmatterName_FallsBackToFileStem()
    {
        const string content = "---\nname: \"1invalid-name!\"\ndescription: test\n---\nbody";
        var src = GetGeneratedSource(RunGenerator("/p/fallback.prompty", content));
        // The class declaration must use the filename-derived name, not the invalid frontmatter name
        Assert.Contains("public static partial class FallbackPrompty", src);
        Assert.DoesNotContain("class 1invalid", src);
        Assert.DoesNotContain("class 1Invalid", src);
    }

    // -----------------------------------------------------------------------
    // TS-13: CTRL-2 — namespace sanitized for path with hyphens/dots
    // -----------------------------------------------------------------------

    [Fact]
    public void TS13_PathWithSpecialChars_NamespaceSanitized()
    {
        var result = RunGenerator(
            "/project/My-Prompts.v2/chat.prompty",
            Fixtures.Chat,
            rootNamespace: "MyApp",
            relativeDir: "My-Prompts.v2/");

        var src = GetGeneratedSource(result);
        Assert.DoesNotContain("My-Prompts", src);
        Assert.DoesNotContain(".v2", src);
        // namespace must be valid C# — no hyphens in identifier
        var tree = CSharpSyntaxTree.ParseText(src);
        var nsErrors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(nsErrors);
    }

    // -----------------------------------------------------------------------
    // TS-14: Reserved keyword parameter names get @ prefix
    // -----------------------------------------------------------------------

    [Fact]
    public void TS14_ReservedKeyword_PrefixedWithAt()
    {
        var src = GetGeneratedSource(RunGenerator("/p/keywords.prompty", Fixtures.Keywords));
        Assert.Contains("@class", src);
        Assert.Contains("@event", src);
    }

    // -----------------------------------------------------------------------
    // TS-15: [GeneratedCode] attribute present
    // -----------------------------------------------------------------------

    [Fact]
    public void TS15_GeneratedCodeAttribute_Present()
    {
        var src = GetGeneratedSource(RunGenerator("/p/chat.prompty", Fixtures.Chat));
        Assert.Contains("[System.CodeDom.Compiler.GeneratedCode", src);
        Assert.Contains("Prompty.SourceGenerator", src);
    }

    // -----------------------------------------------------------------------
    // Fixture strings
    // -----------------------------------------------------------------------

    private static class Fixtures
    {
        public const string Chat =
            "---\ndescription: Basic chat fixture\ninputs:\n  message:\n    kind: string\n    required: true\noutputs:\n  reply:\n    kind: string\n---\nYou are a helpful assistant.";

        public const string GreetingBot =
            "---\nname: GreetingBot\ndescription: Named fixture\ninputs:\n  firstName:\n    kind: string\n    required: true\n  age:\n    kind: integer\n    default: 30\noutputs:\n  result:\n    kind: string\n---\nGreet {{firstName}}.";

        public const string MyAgent =
            "---\ndescription: Hyphenated\ninputs:\n  query:\n    kind: string\n---\nAnswer: {{query}}";

        public const string TypeCheck =
            "---\ndescription: Type mapping\ninputs:\n  flag:\n    kind: boolean\n  score:\n    kind: float\n  tags:\n    kind: array\n  meta:\n    kind: object\n  unknown_field:\n    kind: unknown\noutputs:\n  decision:\n    kind: boolean\n---\nCheck types.";

        public const string MultiOut =
            "---\ndescription: Multi outputs\ninputs:\n  query:\n    kind: string\noutputs:\n  answer:\n    kind: string\n  confidence:\n    kind: float\n---\nMulti-output.";

        public const string Keywords =
            "---\ndescription: Keywords\ninputs:\n  class:\n    kind: string\n  event:\n    kind: string\n---\nKeywords.";
    }
}

// -----------------------------------------------------------------------
// Test infrastructure: AnalyzerConfigOptionsProvider
// -----------------------------------------------------------------------

internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly string? _rootNamespace;
    private readonly string _relativeDir;
    private readonly string _filePath;

    public TestAnalyzerConfigOptionsProvider(string? rootNamespace, string relativeDir, string filePath)
    {
        _rootNamespace = rootNamespace;
        _relativeDir = relativeDir;
        _filePath = filePath;
    }

    public override AnalyzerConfigOptions GlobalOptions => new TestAnalyzerConfigOptions(_rootNamespace, null);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new TestAnalyzerConfigOptions(_rootNamespace, null);

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        if (textFile.Path == _filePath)
            return new TestAnalyzerConfigOptions(_rootNamespace, _relativeDir);
        return new TestAnalyzerConfigOptions(_rootNamespace, null);
    }
}

internal sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly Dictionary<string, string> _values;

    public TestAnalyzerConfigOptions(string? rootNamespace, string? relativeDir)
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rootNamespace != null)
            _values["build_property.RootNamespace"] = rootNamespace;
        if (relativeDir != null)
            _values["build_metadata.AdditionalFiles.RelativeDir"] = relativeDir;
    }

    public override bool TryGetValue(string key, out string value)
    {
        if (_values.TryGetValue(key, out var v))
        {
            value = v;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
