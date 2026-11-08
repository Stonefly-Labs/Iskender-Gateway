using System.Reflection;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>SC-002 and FR-035: every configuration error code is proved by a fixture or a named test.</summary>
public class ErrorCodeCoverageTests
{
    /// <summary>The 15 <c>invalid_request</c> codes of FR-031, each with a folder under <c>invalid/</c>.</summary>
    internal static readonly string[] FixtureCodes =
    [
        "config.document.malformed",
        "config.document.too_large",
        "config.document.duplicate_key",
        "config.document.unsupported_construct",
        "config.namespace.unknown",
        "config.field.unknown",
        "config.field.required",
        "config.field.type_mismatch",
        "config.field.out_of_range",
        "config.field.invalid_format",
        "config.secret.plaintext",
        "config.secret.reference_malformed",
        "config.value.unsafe_for_environment",
        "config.environment.invalid",
        "config.overlay.conflict",
    ];

    public static TheoryData<string> CodeFixtures() =>
        [.. FixtureCodes.SelectMany(code => FixtureLoader.Fixtures($"invalid/{code}"))];

    public static TheoryData<string> ValidFixtures() => [.. FixtureLoader.Fixtures("valid")];

    [Fact]
    public void EveryInvalidRequestConfigurationCodeIsCovered()
    {
        var invalidRequest = ErrorCodes.All.Where(code => code.Since == 2 && code.Category == ErrorCategory.InvalidRequest).Select(code => code.Id);

        Assert.Equal(15, FixtureCodes.Length);
        Assert.Equal(invalidRequest.Order(StringComparer.Ordinal), FixtureCodes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EachCodeHasAtLeastOneFixture()
    {
        Assert.DoesNotContain(FixtureCodes, code => FixtureLoader.Fixtures($"invalid/{code}").Count == 0);
    }

    [Theory]
    [MemberData(nameof(CodeFixtures))]
    public void EachCodeFixtureFailsWithExactlyItsCode(string fixture)
    {
        var code = fixture.Split('/')[1];

        var report = FixtureLoader.AssertMatchesSidecar(fixture);

        Assert.All(report.Errors, error => Assert.Equal(code, error.Code.Id));
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void EveryValidFixturePasses(string fixture)
    {
        var report = FixtureLoader.Validate(fixture);

        Assert.True(report.IsValid, report.ToString());
    }

    [Theory]
    [InlineData(typeof(SchemaRegistryTests), nameof(SchemaRegistryTests.TwoRegistrationsOfOneNamespaceNameBothOwners), "config.schema.duplicate_namespace")]
    [InlineData(typeof(SchemaRegistryTests), nameof(SchemaRegistryTests.AMalformedNamespaceIsInvalid), "config.schema.invalid")]
    [InlineData(typeof(SnapshotTests), nameof(SnapshotTests.OnlyTheRegisteredSchemaInstanceReadsItsSection), "config.section.invalid_access")]
    [InlineData(typeof(SnapshotTests), nameof(SnapshotTests.AnUndeclaredNameOrTheWrongGetterFailsWithNoValueInTheException), "config.section.invalid_access")]
    public void TheInternalCodesAreProvedByNamedTests(Type tests, string method, string code)
    {
        Assert.True(ErrorCodes.TryGet(code, out var registered));
        Assert.Equal(ErrorCategory.Internal, registered!.Category);
        var test = tests.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(test);
        Assert.True(test.IsDefined(typeof(FactAttribute), inherit: true));
    }
}
