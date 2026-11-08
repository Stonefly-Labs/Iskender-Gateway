using System.Text.RegularExpressions;
using Iskender.Core.Contracts.Configuration;
using Iskender.Domain.Registry;
using Microsoft.Extensions.Logging;

namespace Iskender.Domain.Tests.Configuration;

/// <summary>User Story 5 and FR-024, FR-037: the events and the text rendering say which configuration is in force.</summary>
public partial class DiagnosticsTests
{
    private const string ValidatedTemplate =
        "Configuration validated: revision {Revision}, environment {Environment}, sections {Sections}, base {BaseSource}, overlay {OverlaySource}, environment variables {EnvironmentVariableCount}.";

    private const string FieldErrorTemplate = "Configuration error {Code} at {Path} ({Source}:{Line}:{Column}, {Layer}).";

    private const string RejectedTemplate = "Configuration rejected: {ErrorCount} error(s), {ReportedCount} reported, environment {Environment}.";

    [GeneratedRegex(@"^(?<code>[a-z_.]+) at (?<path>\S+) \((?<source>[^:]+):(?<line>\d+):(?<column>\d+), (?<layer>defaults|base|overlay|environment)\): (?<problem>.+)\. Fix: (?<fix>.+)$")]
    private static partial Regex ErrorLine();

    private static object? Value(CapturedEvent captured, string name) => captured.State.Single(pair => pair.Key == name).Value;

    [Fact]
    public void Scenario1AValidConfigurationWritesExactlyOneValidatedEvent()
    {
        var logger = new CapturingLogger();
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(),
            FixtureLoader.Document("overlay/base.yaml"),
            FixtureLoader.Document("overlay/overlay.production.yaml"),
            ConfigurationEnvironmentSource.FromDictionary([KeyValuePair.Create("ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT", "45")]),
            "production"), logger);

        var captured = Assert.Single(logger.Events);
        Assert.Equal(LogLevel.Information, captured.Level);
        Assert.Equal(new EventId(3001, "ConfigurationValidated"), captured.Id);
        Assert.Equal(report.Snapshot!.Revision.Value, Value(captured, "Revision"));
        Assert.Equal("production", Value(captured, "Environment"));
        Assert.Equal("alpha@1,beta@1,persistence@1", Value(captured, "Sections"));
        Assert.Equal("base.yaml", Value(captured, "BaseSource"));
        Assert.Equal("overlay.production.yaml", Value(captured, "OverlaySource"));
        Assert.Equal(1, Value(captured, "EnvironmentVariableCount"));
        Assert.Equal(ValidatedTemplate, Value(captured, "{OriginalFormat}"));
        Assert.Equal(
            $"Configuration validated: revision {report.Snapshot.Revision.Value}, environment production, sections alpha@1,beta@1,persistence@1, base base.yaml, overlay overlay.production.yaml, environment variables 1.",
            captured.Message);
    }

    [Fact]
    public void AnAbsentEnvironmentOrSourceIsWrittenAsNone()
    {
        var logger = new CapturingLogger();
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), environment: ConfigurationEnvironmentSource.FromDictionary(
            [KeyValuePair.Create("ISKENDER__PERSISTENCE__HOST", "db"), KeyValuePair.Create("ISKENDER__PERSISTENCE__POOL__NAME", "main")])), logger);

        var captured = Assert.Single(logger.Events);
        Assert.Equal("(none)", Value(captured, "Environment"));
        Assert.Equal("(none)", Value(captured, "BaseSource"));
        Assert.Equal("(none)", Value(captured, "OverlaySource"));
    }

    [Fact]
    public void Scenario2ARejectedConfigurationWritesOneFieldErrorEventPerErrorThenOneRejectedEvent()
    {
        var logger = new CapturingLogger();
        var report = ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), FixtureLoader.Document("invalid/five-defects.json"), environmentName: "staging"), logger);

        Assert.Equal(6, logger.Events.Count);
        var fieldErrors = logger.Events.Take(5).ToList();
        Assert.All(fieldErrors, captured =>
        {
            Assert.Equal(LogLevel.Warning, captured.Level);
            Assert.Equal(new EventId(3002, "ConfigurationFieldError"), captured.Id);
            Assert.Equal(FieldErrorTemplate, Value(captured, "{OriginalFormat}"));
        });
        Assert.Equal(report.Errors.Select(error => error.Code.Id), fieldErrors.Select(captured => Value(captured, "Code")));
        Assert.Equal(report.Errors.Select(error => error.Path), fieldErrors.Select(captured => Value(captured, "Path")));
        Assert.Equal(report.Errors.Select(error => (object)error.Location.Line), fieldErrors.Select(captured => Value(captured, "Line")));
        Assert.Equal(report.Errors.Select(error => (object)error.Location.Column), fieldErrors.Select(captured => Value(captured, "Column")));
        Assert.All(fieldErrors, captured =>
        {
            Assert.Equal("five-defects.json", Value(captured, "Source"));
            Assert.Equal("base", Value(captured, "Layer"));
        });
        var first = report.Errors[0];
        Assert.Equal(
            $"Configuration error {first.Code.Id} at {first.Path} (five-defects.json:{first.Location.Line}:{first.Location.Column}, base).",
            fieldErrors[0].Message);

        var rejected = logger.Events[5];
        Assert.Equal(LogLevel.Error, rejected.Level);
        Assert.Equal(new EventId(3003, "ConfigurationRejected"), rejected.Id);
        Assert.Equal(RejectedTemplate, Value(rejected, "{OriginalFormat}"));
        Assert.Equal("Configuration rejected: 5 error(s), 5 reported, environment staging.", rejected.Message);
    }

    [Fact]
    public void OnlyTheReportedErrorsAreLoggedWhenMoreThanAHundredExist()
    {
        var logger = new CapturingLogger();
        var report = FixtureLoader.Validate("edge/more-than-100-errors.json");
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(TestSchemas.Registry(), FixtureLoader.Document("edge/more-than-100-errors.json")), logger);

        Assert.Equal(101, logger.Events.Count);
        Assert.Equal(100, logger.Events.Count(captured => captured.Id.Id == 3002));
        Assert.Equal(report.TotalErrorCount, Value(logger.Events[^1], "ErrorCount"));
        Assert.Equal(100, Value(logger.Events[^1], "ReportedCount"));
        Assert.Equal("(none)", Value(logger.Events[^1], "Environment"));
    }

    [Fact]
    public void AnEnvironmentVariableErrorIsLoggedWithItsLayer()
    {
        var logger = new CapturingLogger();
        ConfigurationValidator.Validate(new ConfigurationLoadRequest(
            TestSchemas.Registry(), FixtureLoader.Document("valid/full.json"),
            environment: ConfigurationEnvironmentSource.FromDictionary([KeyValuePair.Create("ISKENDER__PERSISTENCE__HOTS", "x")])), logger);

        var fieldError = logger.Events[0];
        Assert.Equal("ISKENDER__PERSISTENCE__HOTS", Value(fieldError, "Source"));
        Assert.Equal("environment", Value(fieldError, "Layer"));
        Assert.Equal(0, Value(fieldError, "Line"));
    }

    [Fact]
    public void Scenario3TheRenderingHasOneLinePerErrorAndTheRejectedLine()
    {
        var report = FixtureLoader.Validate("invalid/five-defects.json");
        var lines = report.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();

        Assert.Equal(6, lines.Count);
        for (var index = 0; index < 5; index++)
        {
            var match = ErrorLine().Match(lines[index]);
            Assert.True(match.Success, lines[index]);
            var error = report.Errors[index];
            Assert.Equal(error.Code.Id, match.Groups["code"].Value);
            Assert.Equal(error.Path, match.Groups["path"].Value);
            Assert.Equal("five-defects.json", match.Groups["source"].Value);
            Assert.Equal(error.Location.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), match.Groups["line"].Value);
            Assert.Equal(error.Location.Column.ToString(System.Globalization.CultureInfo.InvariantCulture), match.Groups["column"].Value);
            Assert.Equal(error.Fix.TrimEnd('.'), match.Groups["fix"].Value.TrimEnd('.'));
        }

        Assert.Equal("5 configuration error(s); configuration rejected.", lines[^1]);
    }

    [Fact]
    public void Scenario3AValidRenderingIsTheValidLine()
    {
        var report = FixtureLoader.Validate("valid/full.json");

        Assert.Equal($"Configuration valid: revision {report.Snapshot!.Revision.Value}.", report.ToString().Trim());
    }

    [Fact]
    public void Scenario3MoreThanAHundredErrorsRenderTheTruncationLineBeforeTheFinalLine()
    {
        var report = FixtureLoader.Validate("edge/more-than-100-errors.json");
        var lines = report.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();

        Assert.True(report.TotalErrorCount > 100);
        Assert.Equal(102, lines.Count);
        Assert.All(lines.Take(100), line => Assert.Matches(ErrorLine(), line));
        Assert.Equal($"... {report.TotalErrorCount - 100} more error(s) not shown.", lines[100]);
        Assert.Equal($"{report.TotalErrorCount} configuration error(s); configuration rejected.", lines[101]);
    }

    [Fact]
    public void TheContractDocumentStatesTheSameTemplates()
    {
        var contract = File.ReadAllText(Path.Combine(RepositoryRoot(), "specs", "0003-configuration-schema-validation-foundation", "contracts", "configuration.md"));

        Assert.Contains($"| 3001 | `ConfigurationValidated` | Information | `{ValidatedTemplate}` |", contract, StringComparison.Ordinal);
        Assert.Contains($"| 3002 | `ConfigurationFieldError` | Warning | `{FieldErrorTemplate}` |", contract, StringComparison.Ordinal);
        Assert.Contains($"| 3003 | `ConfigurationRejected` | Error | `{RejectedTemplate}` |", contract, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Iskender.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
