using System.Text;
using Iskender.Domain.Registry;
using Iskender.Domain.Registry.Composition;

namespace Iskender.Domain.Tests.Registry.Composition;

/// <summary><see cref="CompositionDocument.OpenRead"/> (maintainer decision, 2026-11-15, <c>implement</c>).</summary>
public sealed class CompositionDocumentTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"isk-document-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private static string Read(Stream stream)
    {
        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    [Fact]
    public void AFileDocumentOpensTheFile()
    {
        File.WriteAllText(_path, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", Read(CompositionDocument.FromFile(_path, ConfigurationDocumentFormat.Json).OpenRead()));
    }

    [Fact]
    public void ATextDocumentOpensItsTextAsUtf8()
    {
        var document = CompositionDocument.FromText("base.yaml", ConfigurationDocumentFormat.Yaml, "name: İskender");

        Assert.Equal("name: İskender", Read(document.OpenRead()));
        Assert.Equal("name: İskender", Read(document.OpenRead()));
    }

    [Fact]
    public void AStreamDocumentCallsItsFactoryOnEachOpenAndNotBefore()
    {
        var calls = 0;
        var document = CompositionDocument.FromStream("base.json", ConfigurationDocumentFormat.Json, () =>
        {
            calls++;
            return new MemoryStream("{}"u8.ToArray());
        });
        Assert.Equal(0, calls);

        Assert.Equal("{}", Read(document.OpenRead()));
        Assert.Equal("{}", Read(document.OpenRead()));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void AMissingFileFailsWhenOpenedNotWhenDescribed()
    {
        var document = CompositionDocument.FromFile(_path, ConfigurationDocumentFormat.Json);

        Assert.Throws<FileNotFoundException>(() => document.OpenRead());
    }
}
