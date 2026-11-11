using System.Reflection;
using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Infrastructure.PostgreSql.Tests.Diagnostics;

/// <summary>
/// Each of the 12 <c>persistence.*</c> codes is asserted by at least one test in this project or in
/// <c>Iskender.Domain.Tests</c>, as the expected value of an assertion on a thrown or recorded <c>.Code</c>, not merely
/// mentioned (SC-007). The list here must match <see cref="ErrorCodes"/> exactly.
/// </summary>
public partial class ErrorCodeCoverageTests
{
    private static readonly string[] PersistenceCodes =
    [
        nameof(ErrorCodes.PersistenceUnavailable),
        nameof(ErrorCodes.PersistenceTimeout),
        nameof(ErrorCodes.PersistenceCancelled),
        nameof(ErrorCodes.PersistenceConcurrencyConflict),
        nameof(ErrorCodes.PersistenceUniqueConflict),
        nameof(ErrorCodes.PersistenceScopeRequired),
        nameof(ErrorCodes.PersistenceScopeNotPermitted),
        nameof(ErrorCodes.PersistenceLockTimeout),
        nameof(ErrorCodes.PersistenceMigrationLocked),
        nameof(ErrorCodes.PersistenceMigrationChecksumMismatch),
        nameof(ErrorCodes.PersistenceSchemaIncompatible),
        nameof(ErrorCodes.PersistenceReverseRequiresConfirmation),
    ];

    public static TheoryData<string> Codes => [.. PersistenceCodes];

    [Fact]
    public void TheListMatchesEveryPersistenceCode()
    {
        var registered = typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.GetValue(null) is ErrorCode code && code.Id.StartsWith("persistence.", StringComparison.Ordinal))
            .Select(field => field.Name);

        Assert.Equal(PersistenceCodes.Order(StringComparer.Ordinal), registered.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EachCodeIsAssertedOnAThrownOrRecordedCode(string code)
    {
        var asserted = TestSources().SelectMany(source => CodeAssertion().Matches(source.Text).Select(match => (source.Path, Name: match.Groups["name"].Value)))
            .Where(hit => hit.Name == code)
            .ToList();

        Assert.True(asserted.Count > 0, $"No test asserts a thrown or recorded code equals ErrorCodes.{code}.");
    }

    [Fact]
    public void TheScanRecognizesOnlyAnAssertionOnACode()
    {
        Assert.Matches(CodeAssertion(), "Assert.Same(ErrorCodes.PersistenceTimeout, (await Assert.ThrowsAsync<GatewayException>(() => Run())).Code);");
        Assert.Matches(CodeAssertion(), "Assert.Equal(ErrorCodes.PersistenceTimeout,\n    unit.Failure!.Code);");
        Assert.DoesNotMatch(CodeAssertion(), "var expected = ErrorCodes.PersistenceTimeout;");
        Assert.DoesNotMatch(CodeAssertion(), "Assert.NotNull(ErrorCodes.PersistenceTimeout);");
        Assert.DoesNotMatch(CodeAssertion(), "Assert.Same(ErrorCodes.PersistenceTimeout, other); var x = failure.Code;");
    }

    // Both test projects' sources, without build output.
    private static List<(string Path, string Text)> TestSources()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Iskender.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("The repository root was not found.");
        }

        string[] projects = ["Iskender.Infrastructure.PostgreSql.Tests", "Iskender.Domain.Tests"];
        return [.. projects.SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, "tests", project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.EndsWith(nameof(ErrorCodeCoverageTests) + ".cs", StringComparison.Ordinal))
            .Select(path => (path, File.ReadAllText(path)))];
    }

    // Assert.Same or Assert.Equal whose expected value is the code and whose actual value, in the same statement, reads a .Code.
    [GeneratedRegex(@"Assert\.(?:Same|Equal)\(\s*ErrorCodes\.(?<name>Persistence\w+)\s*,[^;]*?\.Code\b[^;]*\)\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex CodeAssertion();
}
