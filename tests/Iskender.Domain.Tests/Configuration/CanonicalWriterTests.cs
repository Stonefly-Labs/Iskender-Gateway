using System.Text;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class CanonicalWriterTests
{
    private static readonly SectionSchema Sample = SectionSchema.Create("sample", "Tests.Sample", 3).WithFields(
        FieldSchema.Create("zeta", ConfigurationFieldKind.Text),
        FieldSchema.Create("alpha", ConfigurationFieldKind.Number),
        FieldSchema.Create("aZ", ConfigurationFieldKind.WholeNumber).WithDefault(7),
        FieldSchema.Create("wait", ConfigurationFieldKind.Duration),
        FieldSchema.Create("key", ConfigurationFieldKind.SecretReference),
        FieldSchema.Create("nested", ConfigurationFieldKind.Section).WithFields(FieldSchema.Create("on", ConfigurationFieldKind.Boolean)),
        FieldSchema.Create("list", ConfigurationFieldKind.Array).WithItems(FieldSchema.Create("item", ConfigurationFieldKind.Uri)));

    internal static string Canonical(string json, params SectionSchema[] schemas)
    {
        var registry = ConfigurationSchemaRegistry.Create(schemas.Length == 0 ? [Sample] : schemas);
        var (errors, _, sections) = SchemaValidatorTests.Run(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, json), registry);
        Assert.Empty(errors);
        return Encoding.UTF8.GetString(CanonicalWriter.Write(sections));
    }

    [Fact]
    public void WritesTheFormKeysInOrdinalOrderWithDefaultsWrittenIn()
    {
        Assert.Equal(
            "{\"form\":1,\"iskender\":{\"sample\":{\"aZ\":7,\"alpha\":1.5,\"key\":{\"secretRef\":\"a/b\"},\"list\":[\"https://x.test/a\"],\"nested\":{\"on\":true},\"wait\":90000,\"zeta\":\"é\\\"z\"}},\"schemas\":{\"sample\":3}}",
            Canonical("{\"iskender\":{\"sample\":{\"zeta\":\"é\\\"z\",\"alpha\":1.50,\"wait\":\"90s\",\"key\":{\"secretRef\":\"a/b\"},\"nested\":{\"on\":true},\"list\":[\"https://x.test/a\"]}}}"));
    }

    [Theory]
    [InlineData("1.50")]
    [InlineData("15e-1")]
    [InlineData("1.5")]
    [InlineData("0.015E2")]
    public void EverySpellingOfANumberWritesTheSameDecimal(string number)
    {
        Assert.Contains("\"alpha\":1.5,", Canonical($"{{\"iskender\":{{\"sample\":{{\"alpha\":{number}}}}}}}"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyAndAnAbsentObjectWriteTheSameBytes()
    {
        var absent = Canonical("{\"iskender\":{}}");

        Assert.Equal(absent, Canonical("{\"iskender\":{\"sample\":{}}}"));
        Assert.Equal(absent, Canonical("{\"iskender\":{\"sample\":{\"nested\":{}}}}"));
        Assert.Equal(absent, Canonical(""));
        Assert.Equal("{\"form\":1,\"iskender\":{\"sample\":{\"aZ\":7,\"nested\":{}}},\"schemas\":{\"sample\":3}}", absent);
    }

    [Fact]
    public void NamespacesAreWrittenInOrdinalOrder()
    {
        Assert.Equal(
            "{\"form\":1,\"iskender\":{\"alpha\":{\"name\":\"alpha\"},\"beta\":{\"enabled\":false}},\"schemas\":{\"alpha\":1,\"beta\":1}}",
            Canonical("{}", TestSchemas.Beta, TestSchemas.Alpha));
    }
}
