using System.Text;
using Iskender.Core.Contracts.Configuration;
using Iskender.Core.Contracts.Errors;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class JsonReaderTests
{
    public static TheoryData<string> DocumentFixtures => new(
        FixtureLoader.Fixtures("invalid/config.document.malformed")
            .Concat(FixtureLoader.Fixtures("invalid/config.document.duplicate_key"))
            .Concat(FixtureLoader.Fixtures("invalid/config.document.too_large"))
            .Concat(FixtureLoader.Fixtures("edge"))
            .Where(path => path.EndsWith(".json", StringComparison.Ordinal) && File.Exists(FixtureLoader.SidecarOf(FixtureLoader.FullPath(path))))
            .Where(path => FixtureLoader.Expected(path) is { Count: > 0 } errors && errors.All(error => error.Path == "(document)")));

    private static DocumentRead Read(string text) =>
        JsonDocumentReader.Read(ConfigurationDocumentSource.FromString("t.json", ConfigurationDocumentFormat.Json, text), ConfigurationLayer.Base);

    private static DocumentRead Read(byte[] bytes) =>
        JsonDocumentReader.Read(ConfigurationDocumentSource.FromBytes("t.json", ConfigurationDocumentFormat.Json, bytes), ConfigurationLayer.Base);

    [Theory]
    [MemberData(nameof(DocumentFixtures))]
    public void EachDocumentFixtureStopsWithItsExpectedErrorAndLocation(string fixture)
    {
        var expected = Assert.Single(FixtureLoader.Expected(fixture));

        var read = JsonDocumentReader.Read(FixtureLoader.Document(fixture), ConfigurationLayer.Base);

        Assert.Null(read.Root);
        var error = Assert.IsType<DocumentError>(read.Error);
        Assert.Equal(expected, new ExpectedError(error.Code.Id, "(document)", error.Location.Line, error.Location.Column, ExpectedError.LayerText(error.Location.Layer)));
        Assert.Equal(Path.GetFileName(fixture), error.Location.Source);
    }

    [Fact]
    public void ReadsEveryTokenKindWithItsLexicalKindAndLocation()
    {
        var read = Read("{\n  \"a\": \"text\",\n  \"b\": 1.50,\n  \"c\": true,\n  \"d\": null,\n  \"e\": [1, {}]\n}\n");

        var root = Assert.IsType<ObjectNode>(read.Root);
        Assert.Equal(["a", "b", "c", "d", "e"], root.Members.Select(member => member.Key));
        var a = Assert.IsType<ScalarNode>(root.Find("a")!.Value);
        Assert.Equal(("text", ScalarKind.String, 2, 8), (a.Text, a.Kind, a.Location.Line, a.Location.Column));
        Assert.Equal((2, 3), (root.Find("a")!.KeyLocation.Line, root.Find("a")!.KeyLocation.Column));
        var b = Assert.IsType<ScalarNode>(root.Find("b")!.Value);
        Assert.Equal(("1.50", ScalarKind.Number), (b.Text, b.Kind));
        Assert.Equal(ScalarKind.Boolean, Assert.IsType<ScalarNode>(root.Find("c")!.Value).Kind);
        Assert.Equal(ScalarKind.Null, Assert.IsType<ScalarNode>(root.Find("d")!.Value).Kind);
        var e = Assert.IsType<ArrayNode>(root.Find("e")!.Value);
        Assert.Equal(2, e.Items.Count);
        Assert.IsType<ObjectNode>(e.Items[1]);
    }

    [Fact]
    public void ColumnsCountUnicodeScalarValues()
    {
        var read = Read("{\"é😀\": \"x\"}");

        var member = Assert.IsType<ObjectNode>(read.Root).Members[0];
        Assert.Equal(1, member.KeyLocation.Line);
        Assert.Equal(2, member.KeyLocation.Column);
        Assert.Equal(8, member.Value.Location.Column);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\t\r\n")]
    [InlineData("null")]
    public void AnEmptyDocumentIsAnEmptyRoot(string text)
    {
        var root = Assert.IsType<ObjectNode>(Read(text).Root);

        Assert.Empty(root.Members);
    }

    [Fact]
    public void ADocumentPastTheSizeLimitStopsAtTheBytePastTheLimit()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"x\":\"" + new string('a', 1_048_576) + "\"}");

        var read = Read(bytes);

        Assert.Equal(ErrorCodes.ConfigDocumentTooLarge, read.Error!.Code);
        Assert.Equal((1, 1_048_577), (read.Error.Location.Line, read.Error.Location.Column));
    }

    [Fact]
    public void ADocumentOfExactlyTheSizeLimitIsReadPastTheSizeCheck()
    {
        var prefix = "{\"x\":\"";
        var text = prefix + new string('a', 1_048_576 - prefix.Length - 2) + "\"}";

        var read = Read(Encoding.UTF8.GetBytes(text));

        Assert.Equal(ErrorCodes.ConfigDocumentTooLarge, read.Error!.Code);
        Assert.Equal(6, read.Error.Location.Column);
    }

    [Fact]
    public void ThirtyTwoLevelsAndTenThousandNodesAreAllowed()
    {
        Assert.NotNull(Read(string.Concat(Enumerable.Repeat("{\"a\":", 31)) + "{}" + new string('}', 31)).Root);
        Assert.NotNull(Read("{\"x\":[" + string.Join(',', Enumerable.Repeat("0", 9998)) + "]}").Root);
        Assert.NotNull(Read("{\"x\":\"" + new string('a', 65_536) + "\"}").Root);
    }

    [Fact]
    public void AStringTooLongInUtf8ButShortInCharactersIsTooLarge()
    {
        var read = Read("{\"x\":\"" + string.Concat(Enumerable.Repeat("é", 32_769)) + "\"}");

        Assert.Equal(ErrorCodes.ConfigDocumentTooLarge, read.Error!.Code);
    }

    [Theory]
    [InlineData("{\"a\":1}x")]
    [InlineData("{\"a\":1")]
    [InlineData("{'a':1}")]
    [InlineData("{\"a\":01}")]
    [InlineData("{\"a\":\"\\ud800\"}")]
    [InlineData("\"text\"")]
    [InlineData("5")]
    public void MalformedInputStopsWithOneMalformedError(string text)
    {
        var read = Read(text);

        Assert.Null(read.Root);
        Assert.Equal(ErrorCodes.ConfigDocumentMalformed, read.Error!.Code);
    }

    [Fact]
    public void TheSameKeyInDifferentObjectsIsNotADuplicate()
    {
        Assert.NotNull(Read("{\"a\":{\"x\":1},\"b\":{\"x\":1}}").Root);
    }

    [Fact]
    public void KeysAreCaseSensitiveSoDifferingCaseIsNotADuplicate()
    {
        Assert.Equal(2, Read("{\"a\":1,\"A\":1}").Root!.Members.Count);
    }
}
