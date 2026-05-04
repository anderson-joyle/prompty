using System;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Prompty.SourceGenerator;

[Generator]
public sealed class PromptySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var promptyFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".prompty", StringComparison.OrdinalIgnoreCase));

        var combined = promptyFiles.Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (file, optionsProvider) = pair;
            try
            {
                GenerateForFile(spc, file, optionsProvider);
            }
            catch
            {
                // CTRL-5: never let an unhandled exception propagate to the Roslyn host
            }
        });
    }

    private static void GenerateForFile(
        SourceProductionContext spc,
        AdditionalText file,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        var content = file.GetText(spc.CancellationToken)?.ToString();
        if (string.IsNullOrEmpty(content))
            return;

        // CTRL-5: all parsing wrapped in outer try/catch in Initialize; inner exceptions are suppressed here
        var frontmatter = PromptyFrontmatterReader.TryRead(content!);
        if (frontmatter == null)
            return; // unparseable YAML — skip silently (AC-10)

        var fileOptions = optionsProvider.GetOptions(file);

        fileOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
        fileOptions.TryGetValue("build_metadata.AdditionalFiles.RelativeDir", out var relativeDir);

        var fileStem = Path.GetFileNameWithoutExtension(file.Path);
        var className = PromptyNamingHelper.DeriveClassName(fileStem, frontmatter.Name);
        var namespaceName = PromptyNamingHelper.DeriveNamespace(rootNamespace ?? string.Empty, relativeDir ?? string.Empty);

        var source = PromptyCodeEmitter.Emit(namespaceName, className, content!, frontmatter.Inputs, frontmatter.Outputs);

        var hintName = $"{namespaceName.Replace('.', '_')}_{className}.g.cs";
        spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
    }
}
