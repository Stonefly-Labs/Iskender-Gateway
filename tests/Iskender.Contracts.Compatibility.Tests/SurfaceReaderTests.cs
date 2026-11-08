using Iskender.Contracts.Compatibility.Fixtures.Reader;
using Iskender.Contracts.Compatibility.Tests.Surface;

namespace Iskender.Contracts.Compatibility.Tests;

public class SurfaceReaderTests
{
    private const string Scope = "Iskender.Contracts.Compatibility.Fixtures.Reader";

    [Fact]
    public void ReadsTheFixtureExactly()
    {
        var lines = SurfaceReader.Read(typeof(Sample).Assembly, Scope);

        Assert.Equal(Expected, lines);
    }

    [Fact]
    public void LinesAreSortedOrdinallyAndRoundTrip()
    {
        var lines = SurfaceReader.Read(typeof(Sample).Assembly, Scope);

        Assert.Equal(lines.Order(StringComparer.Ordinal), lines);
        Assert.All(lines, line => Assert.Equal(line, SurfaceLine.Parse(line).ToString()));
    }

    [Fact]
    public void AnEscapedSemicolonSurvivesARoundTrip()
    {
        var line = new SurfaceLine("method", "A::B()", [new("obsolete", "one; two \\ three"), new("static", null)]);

        var parsed = SurfaceLine.Parse(line.ToString());

        Assert.Equal("one; two \\ three", parsed.Get("obsolete"));
        Assert.True(parsed.Has("static"));
    }

    [Fact]
    public void InternalTypesAreNotSurface()
    {
        Assert.DoesNotContain(SurfaceReader.Read(typeof(Sample).Assembly, Scope), line => line.Contains("Hidden", StringComparison.Ordinal));
    }

    private static readonly string[] Expected =
    [
        "ctor Point::.ctor(System.Int32) | access=public; params=x; since=fixture@1",
        "ctor Sample+Nested::.ctor() | access=public",
        "ctor Sample::.ctor(System.String) | access=protected; params=name!; since=fixture@1",
        "enum-value Color::Green | value=2; since=fixture@1",
        "enum-value Color::Red | value=1; since=fixture@1",
        "event Sample::OnChanged | access=public; type=Changed?; since=fixture@1",
        "field Sample::Default | access=public; static; readonly; type=Sample?; since=fixture@1",
        "field Sample::Limit | access=public; const; type=System.Int32; value=3; since=fixture@1",
        "method Changed::Invoke(System.Object) | access=public; returns=System.Void; params=sender?; since=fixture@1",
        "method IShape::Sides() | access=public; virtual; returns=System.Int32; since=fixture@1",
        "method Sample::Echo``1(T) | access=public; static; returns=T?; params=value?; since=fixture@1",
        "method Sample::Map(System.String[]) | access=public; returns=System.Collections.Generic.Dictionary`2<System.String!,System.Collections.Generic.List`1<System.Nullable`1<System.Int32>>!>?; params=keys:System.String?[]!; since=fixture@1",
        "method Sample::Old() | access=public; returns=System.Void; obsolete=Deprecated in fixture@2. No replacement.; since=fixture@1",
        "method Sample::Paint(Color) | access=public; abstract; returns=Color; params=color=2; since=fixture@1",
        "method Sample::TryFind(System.String,Sample&,System.Int32) | access=public; virtual; returns=System.Boolean; params=key!,out found?,depth=2; since=fixture@1",
        "property IShape::Name | access=public; abstract; type=System.String!; get=public; since=fixture@1",
        "property Point::X | access=public; type=System.Int32; get=public; since=fixture@1",
        "property Sample+Nested::Value | access=public; type=System.Int32; get=public; set=public",
        "property Sample::Label | access=public; type=System.String?; get=public; set=protected; since=fixture@1",
        "property Sample::Name | access=public; type=System.String!; get=public; since=fixture@1",
        "property Sample::Order | access=public; type=System.Int32; get=public; init=public; since=fixture@1",
        "property Sample::Tags | access=public; type=System.Collections.Generic.IReadOnlyList`1<System.String?>!; get=public; since=fixture@2",
        "type Changed | access=public; kind=delegate; since=fixture@1",
        "type Color | access=public; kind=enum; underlying=System.Byte; since=fixture@1",
        "type IShape | access=public; kind=interface; since=fixture@1",
        "type Point | access=public; kind=struct; since=fixture@1",
        "type Sample | access=public; abstract; kind=class; interfaces=IShape; since=fixture@1",
        "type Sample+Nested | access=public; sealed; kind=class",
    ];
}
