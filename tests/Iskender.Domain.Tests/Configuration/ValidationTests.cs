using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>User Story 1: a document is validated against the registered schemas, and every defect is reported at once.</summary>
public class ValidationTests
{
    public static TheoryData<string> NegativeFixtures() =>
        [.. FixtureLoader.Fixtures("invalid").Concat(FixtureLoader.Fixtures("edge")).Where(path => FixtureLoader.Expected(path).Count > 0)];

    [Fact]
    public void Scenario1AValidDocumentGivesASnapshotARevisionAndNoErrors()
    {
        var report = FixtureLoader.Validate("valid/full.json");

        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
        Assert.NotNull(report.Snapshot);
        Assert.Matches("^cfg-[a-z2-7]{52}$", report.Snapshot.Revision.Value);
        var persistence = report.Snapshot.GetSection(TestSchemas.Persistence);
        Assert.Equal("db-1.example", persistence.GetString("host"));
        Assert.Equal(2, persistence.GetSections("replicas")!.Count);
        Assert.True(report.Snapshot.GetSection(TestSchemas.Beta).GetBoolean("enabled"));
    }

    [Fact]
    public void Scenario2AnUnknownFieldNearAKnownOneSuggestsIt()
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar("invalid/unknown-field-near-miss.json").Errors);

        Assert.Contains("connectionTimeout", error.Fix, StringComparison.Ordinal);
    }

    /// <summary>Every fixture's undeclared key that is within an edit distance of 2 of a declared sibling, with that sibling.</summary>
    public static TheoryData<string, string, string> NearMisses => new()
    {
        { "invalid/unknown-field-near-miss.json", "iskender.persistence.conectionTimeout", "connectionTimeout" },
        { "invalid/config.field.unknown/unknown-field.json", "iskender.persistence.hots", "host" },
        { "invalid/config.field.unknown/beside-secret-ref.json", "iskender.alpha.nmae", "name" },
        { "edge/variable-errors-do-not-stop-validation.json", "iskender.persistence.HOTS", "host" },
    };

    [Theory]
    [MemberData(nameof(NearMisses))]
    public void EveryNearMissFixtureSuggestsTheClosestDeclaredSibling(string fixture, string path, string sibling)
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar(fixture).Errors, error => error.Path == path);

        Assert.Equal("config.field.unknown", error.Code.Id);
        Assert.Contains(sibling, error.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryNearMissInTheFixturesIsListed()
    {
        var listed = NearMisses.Select(row => (Fixture: row.Data.Item1, Path: row.Data.Item2)).ToHashSet();
        var found = NegativeFixtures().Select(row => row.Data)
            .SelectMany(fixture => FixtureLoader.Expected(fixture)
                .Where(error => error.Code == "config.field.unknown" && SiblingsOf(error.Path) is { } siblings
                    && siblings.Any(name => NameSuggester.Distance(LastSegment(error.Path), name) <= 2))
                .Select(error => (Fixture: fixture, error.Path)))
            .ToHashSet();

        Assert.Equal(found.Order(), listed.Order());
    }

    private static string LastSegment(string path) => path[(path.LastIndexOf('.') + 1)..];

    /// <summary>The declared names beside the path's last segment in the test schemas, or null when its parent is not a declared object.</summary>
    private static IEnumerable<string>? SiblingsOf(string path)
    {
        var segments = path.Split('.');
        IReadOnlyList<FieldSchema>? fields = TestSchemas.Registry().Schemas.FirstOrDefault(schema => schema.Namespace == segments[1])?.Fields;
        foreach (var segment in segments[2..^1])
        {
            var name = segment.Split('[')[0];
            var field = fields?.FirstOrDefault(candidate => candidate.Name == name);
            fields = field?.Kind == ConfigurationFieldKind.Array ? field.Items?.Fields : field?.Fields;
        }

        return segments.Length < 3 ? null : fields?.Select(field => field.Name);
    }

    [Fact]
    public void Scenario3AnOutOfRangeValueStatesTheRange()
    {
        var error = Assert.Single(FixtureLoader.AssertMatchesSidecar("invalid/out-of-range.json").Errors);

        Assert.Contains("from 1 to 300", error.Problem, StringComparison.Ordinal);
        Assert.Contains("from 1 to 300", error.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void Scenario4TheRejectedValueAppearsNowhereInTheReport()
    {
        var report = FixtureLoader.AssertMatchesSidecar("invalid/out-of-range-distinctive.json");
        var error = Assert.Single(report.Errors);

        Assert.Contains("from 1 to 300", error.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("4271", error.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("4271", error.Fix, StringComparison.Ordinal);
        Assert.DoesNotContain("4271", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("4271", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scenario5FiveDefectsArriveInOneReportInSourceOrder()
    {
        var report = FixtureLoader.AssertMatchesSidecar("invalid/five-defects.json");

        Assert.Equal(5, report.TotalErrorCount);
        Assert.Equal(
            ["config.field.unknown", "config.field.type_mismatch", "config.field.out_of_range", "config.field.required", "config.field.invalid_format"],
            report.Errors.Select(error => error.Code.Id));
        Assert.Equal(report.Errors.OrderBy(error => error.Location.Line).ThenBy(error => error.Location.Column), report.Errors);
    }

    [Theory]
    [MemberData(nameof(NegativeFixtures))]
    public void EveryFailureHasNoSnapshotNoRevisionAndActionableErrors(string fixture)
    {
        var report = FixtureLoader.AssertMatchesSidecar(fixture);

        Assert.False(report.IsValid);
        Assert.Null(report.Snapshot);
        Assert.Null(report.Snapshot?.Revision);

        // SC-006: every error has a path, a location, and a fix.
        Assert.All(report.Errors, error =>
        {
            Assert.False(string.IsNullOrWhiteSpace(error.Path));
            Assert.NotNull(error.Location);
            Assert.False(string.IsNullOrWhiteSpace(error.Location.Source));
            Assert.False(string.IsNullOrWhiteSpace(error.Fix));
        });
    }
}
