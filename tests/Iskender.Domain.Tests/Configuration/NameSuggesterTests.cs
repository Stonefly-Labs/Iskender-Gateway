using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

public class NameSuggesterTests
{
    private static readonly string[] Siblings = ["connectionTimeout", "host", "idleTimeout", "mode"];

    [Theory]
    [InlineData("conenctionTimeout")]
    [InlineData("conectionTimeout")]
    [InlineData("connectionTimeot")]
    [InlineData("ConnectionTimeout")]
    [InlineData("CONNECTIONTIMEOUT")]
    [InlineData("connectiontimeoutxy")]
    public void NamesTheClosestSiblingWithinTwoEdits(string name)
    {
        Assert.Equal("connectionTimeout", NameSuggester.Suggest(name, Siblings).Closest);
    }

    [Fact]
    public void ATranspositionCountsAsOneEdit()
    {
        Assert.Equal(1, NameSuggester.Distance("conenctionTimeout", "connectionTimeout"));
        Assert.Equal(1, NameSuggester.Distance("ab", "ba"));
    }

    [Fact]
    public void ADeletionCountsAsOneEdit()
    {
        Assert.Equal(1, NameSuggester.Distance("conectionTimeout", "connectionTimeout"));
    }

    [Fact]
    public void DistanceThreeListsTheSiblingsInstead()
    {
        var (closest, listed) = NameSuggester.Suggest("cnctionTimeout", Siblings);

        Assert.Equal(3, NameSuggester.Distance("cnctionTimeout", "connectionTimeout"));
        Assert.Null(closest);
        Assert.Equal(Siblings, listed);
    }

    [Fact]
    public void ATieGoesToTheOrdinalFirstName()
    {
        Assert.Equal("hostA", NameSuggester.Suggest("host", ["hostB", "hostA"]).Closest);
    }

    [Fact]
    public void AParentWithMoreThanTenFieldsListsTheFirstTenInOrdinalOrder()
    {
        var siblings = Enumerable.Range(0, 15).Select(index => $"field{(char)('o' - index)}").ToList();

        var (closest, listed) = NameSuggester.Suggest("unrelatedName", siblings);

        Assert.Null(closest);
        Assert.Equal(siblings.Order(StringComparer.Ordinal).Take(10), listed);
    }

    [Fact]
    public void FieldUnknownTextNamesTheSectionAndTheSuggestion()
    {
        var text = ErrorTextTemplates.FieldUnknown("persistence", "conectionTimeout", Siblings);

        Assert.Equal("the field is not declared by section persistence", text.Problem);
        Assert.Equal("did you mean connectionTimeout?", text.Fix);
        Assert.Equal("use a declared field: connectionTimeout, host, idleTimeout, mode.", ErrorTextTemplates.FieldUnknown("persistence", "zzzzzzz", Siblings).Fix);
    }

    [Fact]
    public void OutOfRangeTextStatesTheRangeFromTheSchema()
    {
        var field = FieldSchema.Create("connectionTimeout", ConfigurationFieldKind.WholeNumber).WithConstraints(FieldConstraints.None.WithIntegerRange(1, 300));

        var text = ErrorTextTemplates.OutOfRange(field);

        Assert.Equal("must be an integer from 1 to 300", text.Problem);
        Assert.Equal("set a value from 1 to 300.", text.Fix);
    }

    [Theory]
    [InlineData(1_500, "1500ms")]
    [InlineData(30_000, "30s")]
    [InlineData(300_000, "5m")]
    [InlineData(86_400_000, "1d")]
    [InlineData(0, "0ms")]
    public void DurationsAreWrittenInTheLargestEvenUnit(long milliseconds, string text)
    {
        Assert.Equal(text, ErrorTextTemplates.Text(TimeSpan.FromMilliseconds(milliseconds)));
    }
}
