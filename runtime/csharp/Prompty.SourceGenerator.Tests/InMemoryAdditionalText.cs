using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Prompty.SourceGenerator.Tests;

internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly string _path;
    private readonly string _content;

    public InMemoryAdditionalText(string path, string content)
    {
        _path = path;
        _content = content;
    }

    public override string Path => _path;

    public override SourceText? GetText(CancellationToken cancellationToken = default)
        => SourceText.From(_content, Encoding.UTF8);
}
